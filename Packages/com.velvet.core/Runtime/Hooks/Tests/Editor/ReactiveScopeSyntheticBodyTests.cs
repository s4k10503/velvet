using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the weaver reads a component body that no C# source in this repository produces, by assembling
    /// the body with Cecil in a module of its own, weaving it through the editor-only <c>Unity.Velvet.CodeGen</c>
    /// assembly and reading back the report and the IL.
    /// <list type="bullet">
    /// <item>A body whose evaluation stack the walk cannot follow is cached as a whole and in no part, and a body whose
    /// last instruction falls through is still followed to its end.</item>
    /// <item>No more than 32 scopes are cut from a body, and the report says how many were left out.</item>
    /// <item>A hook in a filter leaves the body alone, and a call is a tree only through 32 base types.</item>
    /// <item>A branch to a return is moved onto the instructions that store the tree, a statement at the start of a
    /// body that only the first run uses belongs to that run, and a short branch stays in range.</item>
    /// <item>A property read with a non-virtual call and a field holding a pointer are keyed as the weaver says, and a
    /// local holding a pointer is declined.</item>
    /// <item>A report is a build warning only for an assembly that asks for it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeSyntheticBodyTests
    {
        private const string CodeGenAssemblyName = "Unity.Velvet.CodeGen";
        private const string StackShapeNote =
            "No part of the body is cached on its own: its instructions use a stack shape the build does not follow.";

        private static readonly MethodInfo s_text = typeof(V).GetMethod(nameof(V.Text), new[] { typeof(string) })!;
        private static readonly MethodInfo s_fragment = typeof(V).GetMethod(nameof(V.Fragment), new[] { typeof(VNode[]) })!;
        private static readonly MethodInfo s_toStringOfInt = typeof(Convert).GetMethod(nameof(Convert.ToString), new[] { typeof(int) })!;

        #region What the tests share

        private sealed class Woven : IDisposable
        {
            public ModuleDefinition Module = null!;
            public MethodDefinition Method = null!;
            public List<string> Diagnostics = new();
            public bool? BodyGate;
            public int? Scopes;
            public List<string> Notes = new();

            public void Dispose() => Module.Dispose();
        }

        // Resolves a reference to an assembly that is loaded in the editor, and to nothing else, so a module
        // assembled here can name Velvet without a search path.
        private sealed class LoadedAssemblyResolver : IAssemblyResolver
        {
            private readonly Dictionary<string, AssemblyDefinition?> _read = new();

            public AssemblyDefinition? Resolve(AssemblyNameReference name) => ReadByName(name.Name);

            public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) => ReadByName(name.Name);

            public void Dispose()
            {
            }

            private AssemblyDefinition? ReadByName(string name)
            {
                if (_read.TryGetValue(name, out var known)) return known;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(each => each.GetName().Name == name);
                var location = loaded == null || loaded.IsDynamic ? string.Empty : loaded.Location;
                return _read[name] = string.IsNullOrEmpty(location) ? null : AssemblyDefinition.ReadAssembly(location);
            }
        }

        private static ModuleDefinition NewModule()
            => ModuleDefinition.CreateModule("SyntheticComponents",
                new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = new LoadedAssemblyResolver() });

        private static TypeDefinition AddStaticClass(ModuleDefinition module, string name)
        {
            var type = new TypeDefinition("Synthetic", name,
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(type);
            return type;
        }

        private static MethodDefinition AddComponent(ModuleDefinition module, params TypeReference[] parameters)
        {
            var method = new MethodDefinition("Build",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.ImportReference(typeof(VNode)));
            foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
            method.CustomAttributes.Add(new CustomAttribute(
                module.ImportReference(typeof(ComponentAttribute).GetConstructor(Type.EmptyTypes))));
            method.Body = new Mono.Cecil.Cil.MethodBody(method) { InitLocals = true };
            AddStaticClass(module, "Components").Methods.Add(method);
            return method;
        }

        // The body is read back from bytes before it is woven, as the post-processor reads an assembly, so that every
        // instruction carries the offset the weaver compares.
        private static Woven Weave(ModuleDefinition assembled, MethodDefinition assembledMethod)
        {
            var stream = new MemoryStream();
            assembled.Write(stream);
            stream.Position = 0;
            assembled.Dispose();
            var module = ModuleDefinition.ReadModule(stream,
                new ReaderParameters { AssemblyResolver = new LoadedAssemblyResolver() });
            var method = module.GetType("Synthetic", "Components").Methods.Single(each => each.Name == assembledMethod.Name);
            var codeGen = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == CodeGenAssemblyName);
            Assume.That(codeGen, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var weave = codeGen!.GetType("Velvet.CodeGen.CompilerWeaver", throwOnError: true)!
                .GetMethod("Weave", BindingFlags.Public | BindingFlags.Static)!;
            var diagnostics = Activator.CreateInstance(weave.GetParameters()[1].ParameterType)!;
            weave.Invoke(null, new[] { (object)module, diagnostics });

            var messages = new List<string>();
            foreach (var diagnostic in (IEnumerable)diagnostics)
            {
                messages.Add(diagnostic.GetType().GetProperty("MessageData")?.GetValue(diagnostic) as string ?? string.Empty);
            }
            var report = method.CustomAttributes.FirstOrDefault(
                attribute => attribute.AttributeType.FullName == typeof(MemoizationReportAttribute).FullName);
            return new Woven
            {
                Module = module,
                Method = method,
                Diagnostics = messages,
                BodyGate = report == null ? null : (bool)report.ConstructorArguments[0].Value,
                Scopes = report == null ? null : (int)report.ConstructorArguments[1].Value,
                Notes = report == null
                    ? new List<string>()
                    : ((string)report.ConstructorArguments[2].Value).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            };
        }

        // `Build(a)` over strings: a fragment of the texts of `count` parameters, each cut as a scope of its own.
        private static void AppendAFragmentOfTexts(ModuleDefinition module, MethodDefinition method, int count)
        {
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4, count));
            il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(VNode))));
            for (var i = 0; i < count; i++)
            {
                il.Append(il.Create(OpCodes.Dup));
                il.Append(il.Create(OpCodes.Ldc_I4, i));
                il.Append(il.Create(OpCodes.Ldarg, method.Parameters[i]));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
                il.Append(il.Create(OpCodes.Stelem_Ref));
            }
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_fragment)));
            il.Append(il.Create(OpCodes.Ret));
        }

        // `Build(..., last)`: a fragment of two children, the first built by `firstChild` and the second a text of the last parameter.
        private static void AppendAFragmentOfTwoChildren(ModuleDefinition module, MethodDefinition method, Action<ILProcessor> firstChild)
        {
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4_2));
            il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(VNode))));
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            firstChild(il);
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ldarg, method.Parameters[method.Parameters.Count - 1]));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_fragment)));
            il.Append(il.Create(OpCodes.Ret));
        }

        #endregion

        #region A stack shape the walk cannot follow

        // GREEN_ON_BASE(characterization): the base reports that no part of a body is cached when a branch jumps to itself with a value on the stack.
        // Reading `>` as `>=` where `Deliver` tells a forward branch from a backward one hands the state on as a join
        // of equal depth, and reading the `Failed = true` that follows as `false` lets the walk go on. Removing the note
        // that `PlanScopes` adds loses the sentence.
        [Test]
        public void Given_ABranchThatJumpsToItselfWithAValueOnTheStack_When_Woven_Then_TheReportSaysTheStackShapeIsNotFollowed()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var spin = il.Create(OpCodes.Br, il.Create(OpCodes.Nop));
            spin.Operand = spin;
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(spin);
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes, Does.Contain(StackShapeNote));
        }

        // GREEN_ON_BASE(characterization): the base reports the same where a backward branch carries a value to a loop head with an empty stack.
        // Reading `!=` as `==` where `Deliver` tests the depth of the state being sent looks only at the depth of the
        // head, which is empty, and records a loop the walk then follows.
        [Test]
        public void Given_ABackwardBranchCarryingAValueToAnEmptyLoopHead_When_Woven_Then_TheReportSaysTheStackShapeIsNotFollowed()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var head = il.Create(OpCodes.Nop);
            il.Append(head);
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Br, head));
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes, Does.Contain(StackShapeNote));
        }

        // GREEN_ON_BASE(characterization): the base reports the same where two paths reach an instruction with stacks of different depth.
        // Reading the `Failed = true` that `Join` sets on a mismatch as `false` lets the walk go on with the first path's stack.
        [Test]
        public void Given_TwoPathsReachingOneInstructionWithDifferentDepths_When_Woven_Then_TheReportSaysTheStackShapeIsNotFollowed()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var join = il.Create(OpCodes.Ldnull);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brtrue, join));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(join);
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes, Does.Contain(StackShapeNote));
        }

        // GREEN_ON_BASE(characterization): the base reports the same where an instruction takes a value off an empty stack.
        // Reading the `Failed = true` that `Fail` sets as `false` lets the walk go on past an instruction it could not apply.
        [Test]
        public void Given_AnInstructionThatPopsAnEmptyStack_When_Woven_Then_TheReportSaysTheStackShapeIsNotFollowed()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Pop));
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes, Does.Contain(StackShapeNote));
        }

        // GREEN_ON_BASE(characterization): the base weaves a body whose last instruction falls through, because the walk stops at the end of the body.
        // Reading `>=` as `>` where `Join` tests the target against the instruction count indexes past the end,
        // which leaves the body unwoven with a warning and no report.
        [Test]
        public void Given_ABodyWhoseLastInstructionFallsThrough_When_Woven_Then_TheBodyIsKeyedAsAWhole()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var end = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brtrue, end));
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(end);

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.BodyGate, Is.True);
        }

        #endregion

        #region The number of scopes

        // GREEN_ON_BASE(characterization): the base cuts the first 32 of 33 scopes that each read a parameter of their own, and says how many it left out.
        // Reading `-` as `+` where `Select` trims the list throws and leaves the body unwoven, and removing the note,
        // or the `AddRange` that puts the notes in the report, loses the sentence.
        [Test]
        public void Given_ThirtyThreeScopesEachReadingAParameter_When_Woven_Then_ThirtyTwoAreCutAndTheNoteCountsTheOneLeftOut()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, Enumerable.Repeat(module.TypeSystem.String, 33).ToArray());
            AppendAFragmentOfTexts(module, method, 33);

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That($"{woven.Scopes}|{woven.Notes.SingleOrDefault(note => note.Contains("further scopes"))}",
                Is.EqualTo("32|1 further scopes were left uncached to keep the cost of the cache bounded"));
        }

        // GREEN_ON_BASE(characterization): the base cuts exactly 32 scopes without a note about any left out.
        // Reading `>` as `>=` where `Select` compares the count with the limit adds a note about none.
        [Test]
        public void Given_ThirtyTwoScopesEachReadingAParameter_When_Woven_Then_AllAreCutAndNothingIsSaidToBeLeftOut()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, Enumerable.Repeat(module.TypeSystem.String, 32).ToArray());
            AppendAFragmentOfTexts(module, method, 32);

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That($"{woven.Scopes}|{woven.Notes.Count}", Is.EqualTo("32|0"));
        }

        #endregion

        #region A report as a warning

        // GREEN_ON_BASE(characterization): the base reports nothing as a build warning for an assembly whose only attribute is some other.
        // Removing the guard of `WarnsOnReport` that skips an attribute which is not the opt-in turns any assembly
        // attribute into the opt-in and reports the body's note.
        [Test]
        public void Given_AnAssemblyCarryingOnlyAnUnrelatedAttribute_When_ABodyHasANote_Then_NoWarningIsReported()
        {
            // Arrange
            var module = NewModule();
            var attribute = new CustomAttribute(module.ImportReference(
                typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "Velvet.CodeShape"));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "enforce"));
            module.Assembly.CustomAttributes.Add(attribute);
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(Environment).GetProperty(nameof(Environment.NewLine))!.GetMethod)));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That($"{woven.Notes.Count > 0}|{string.Join(", ", woven.Diagnostics)}", Is.EqualTo("True|"));
        }

        #endregion

        #region Where a body returns

        // GREEN_ON_BASE(characterization): the base commits the cache on a path that jumps straight to the return.
        // No branch is left targeting the `ret` itself. Removing the `RetargetBranches` call from `InjectMemoization`
        // leaves the jump past the instructions that store the tree.
        [Test]
        public void Given_ABodyWhoseBranchJumpsStraightToTheReturn_When_Woven_Then_NoBranchTargetsTheReturn()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var second = il.Create(OpCodes.Ldarg_0);
            var ret = il.Create(OpCodes.Ret);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brtrue, second));
            il.Append(il.Create(OpCodes.Ldstr, "first"));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Br, ret));
            il.Append(second);
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(ret);

            // Act
            using var woven = Weave(module, method);
            var targetingAReturn = woven.Method.Body.Instructions
                .Count(instruction => instruction.Operand is Instruction target && target.OpCode.Code == Code.Ret);

            // Assert
            Assert.That($"{woven.BodyGate}|{targetingAReturn}", Is.EqualTo("True|0"));
        }

        #endregion

        #region A hook in a filter

        // GREEN_ON_BASE(characterization): the base leaves a body alone whose hook sits in the filter of a catch block that nothing jumps over.
        // The try block ends in `throw` and the filter holds only the hook, so no branch skips it. Reading the third
        // `return true` of `HookInsideHandler` as `false` lets the body through.
        [Test]
        public void Given_AHookInTheFilterOfACatchBlockAfterATryThatThrows_When_Woven_Then_TheReportSaysTheComponentIsNotCached()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            var il = method.Body.GetILProcessor();
            var tryStart = il.Create(OpCodes.Ldnull);
            var filterStart = il.Create(OpCodes.Pop);
            var handlerStart = il.Create(OpCodes.Pop);
            var end = il.Create(OpCodes.Ldarg_0);
            il.Append(tryStart);
            il.Append(il.Create(OpCodes.Throw));
            il.Append(filterStart);
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(Hooks).GetMethod(nameof(Hooks.UseId)))));
            il.Append(il.Create(OpCodes.Pop));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Endfilter));
            il.Append(handlerStart);
            il.Append(il.Create(OpCodes.Leave, end));
            il.Append(end);
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Ret));
            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Filter)
            {
                TryStart = tryStart,
                TryEnd = filterStart,
                FilterStart = filterStart,
                HandlerStart = handlerStart,
                HandlerEnd = end,
                CatchType = module.ImportReference(typeof(Exception)),
            });

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes, Does.Contain("The component is not cached: a hook call sits inside a try, catch or finally."));
        }

        #endregion

        #region A run that begins at the start of the body

        // GREEN_ON_BASE(characterization): the base takes the statement at the very start of a body that only the first run uses into that run.
        // The instruction that copies the parameter into the local follows the gate of the scope that reads the local.
        // Reading `<` as `<=` where `AbsorbSetup` tests for a statement that begins at instruction 0 leaves that
        // statement ahead of the gate.
        [Test]
        public void Given_AStatementAtTheStartOfAHooklessBodyThatOnlyTheFirstRunUses_When_Woven_Then_TheStatementFollowsTheGateOfThatRun()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String, module.TypeSystem.String);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(VNode))));
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldc_I4_2));
            il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(VNode))));
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(s_fragment)));
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);
            var instructions = woven.Method.Body.Instructions.ToList();
            var gates = instructions.Select((instruction, index) => (instruction, index))
                .Where(each => ReactiveScopeTestSupport.IsGateCall(each.instruction))
                .Select(each => each.index)
                .ToList();
            var copy = instructions.FindIndex(instruction => instruction.OpCode.Code == Code.Stloc_0
                || (instruction.OpCode.Code is Code.Stloc or Code.Stloc_S && ((VariableDefinition)instruction.Operand).Index == 0));

            // Assert
            Assert.That((gates.Count, gates.Count > 1 && gates[1] < copy), Is.EqualTo((3, true)));
        }

        #endregion

        #region What a scope reads

        private sealed class Item
        {
            public string Tag { get; } = "tag";
        }

        // GREEN_ON_BASE(characterization): the base keys a scope on the getter of a property read with a non-virtual call.
        // The key reads the property itself, with a virtual call, and not the object it is read off. Reading `!=` as
        // `==` where `MemberReadAt` tests the opcode of the instruction after the load reads only a virtual call, and
        // leaves a non-virtual one to key the scope on the object. Roslyn calls a getter virtually whatever it is read
        // off, so only IL written some other way holds one.
        [Test]
        public void Given_APropertyReadWithANonVirtualCall_When_Woven_Then_TheKeyReadsThePropertyWithAVirtualCall()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.ImportReference(typeof(Item)), module.TypeSystem.String);
            AppendAFragmentOfTwoChildren(module, method, il =>
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(Item).GetProperty(nameof(Item.Tag))!.GetMethod)));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            });

            // Act
            using var woven = Weave(module, method);
            var virtualReads = woven.Method.Body.Instructions.Count(instruction => instruction.OpCode.Code == Code.Callvirt
                && instruction.Operand is MethodReference callee && callee.Name == "get_" + nameof(Item.Tag));

            // Assert
            Assert.That(virtualReads, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base keys a scope on the object whose field holds a pointer, and not on the field.
        // Removing the `IsPointer` clause from `IsStorable` takes the pointer for a value the key can store and reads
        // it in the key, which puts a second load of the field in the method.
        [Test]
        public void Given_AFieldHoldingAPointerReadOffAParameter_When_Woven_Then_TheKeyDoesNotReadTheField()
        {
            // Arrange
            var module = NewModule();
            var holder = new TypeDefinition("Synthetic", "Holder", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var pointer = new FieldDefinition("Pointer", Mono.Cecil.FieldAttributes.Public, new PointerType(module.TypeSystem.Int32));
            holder.Fields.Add(pointer);
            module.Types.Add(holder);
            var method = AddComponent(module, holder, module.TypeSystem.String);
            AppendAFragmentOfTwoChildren(module, method, il =>
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldfld, pointer));
                il.Append(il.Create(OpCodes.Conv_I4));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_toStringOfInt)));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            });

            // Act
            using var woven = Weave(module, method);
            var loads = woven.Method.Body.Instructions.Count(instruction => instruction.OpCode.Code == Code.Ldfld
                && instruction.Operand is FieldReference field && field.Name == pointer.Name);

            // Assert
            Assert.That(loads, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base declines a scope that reads a local holding a pointer.
        // Removing the `IsPointer` clause from the test in `AddLocal` takes the pointer for a value the key can store.
        [Test]
        public void Given_ALocalHoldingAPointerReadByAScope_When_Woven_Then_TheReportSaysItReadsALocalThatHoldsAReference()
        {
            // Arrange
            var module = NewModule();
            var method = AddComponent(module, module.TypeSystem.String);
            method.Body.Variables.Add(new VariableDefinition(new PointerType(module.TypeSystem.Int32)));
            AppendAFragmentOfTwoChildren(module, method, il =>
            {
                il.Append(il.Create(OpCodes.Ldloc_0));
                il.Append(il.Create(OpCodes.Conv_I4));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_toStringOfInt)));
                il.Append(il.Create(OpCodes.Call, module.ImportReference(s_text)));
            });

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Notes.Any(note => note.Contains("it reads a local that holds a reference")), Is.True);
        }

        #endregion

        #region A chain of base types

        // GREEN_ON_BASE(characterization): the base follows 32 base types, so a call returning a type that reaches the tree only past them is no tree.
        // Reading `<` as `<=` where `ResolvesToVNode` bounds the walk finds the tree one base type higher and cuts the call.
        [Test]
        public void Given_ACallReturningATypeThatDerivesFromATreeThroughThirtyTwoBaseTypes_When_Woven_Then_OnlyTheTextIsCut()
        {
            // Arrange
            var module = NewModule();
            TypeReference previous = module.ImportReference(typeof(VNode));
            for (var depth = 0; depth < 32; depth++)
            {
                var link = new TypeDefinition("Chain", "Link" + depth,
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, previous);
                module.Types.Add(link);
                previous = link;
            }
            var make = new MethodDefinition("Make",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, previous);
            make.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            make.Body = new Mono.Cecil.Cil.MethodBody(make);
            make.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
            make.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            var method = AddComponent(module, module.TypeSystem.String, module.TypeSystem.String);
            method.DeclaringType.Methods.Add(make);
            AppendAFragmentOfTwoChildren(module, method, il =>
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Call, make));
            });

            // Act
            using var woven = Weave(module, method);

            // Assert
            Assert.That(woven.Scopes, Is.EqualTo(1));
        }

        #endregion

        #region An assembly whose Velvet has no scope API

        // The memoization methods of the one-gate form, declared in the module itself so that the weaver finds them
        // there and finds no ReserveScopes, TryGetScope or StoreScope beside them.
        private static MethodDefinition AddAComponentOverAVelvetWithoutScopes(ModuleDefinition module)
        {
            const Mono.Cecil.TypeAttributes publicClass = Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class;
            const Mono.Cecil.MethodAttributes publicStatic = Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static;
            var vnode = new TypeDefinition("Velvet", "VNode", publicClass, module.TypeSystem.Object);
            var attribute = new TypeDefinition("Velvet", "ComponentAttribute", publicClass, module.ImportReference(typeof(Attribute)));
            var constructor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            constructor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            attribute.Methods.Add(constructor);
            var hooks = new TypeDefinition("Velvet", "Hooks", publicClass | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            var tryGet = new MethodDefinition(nameof(Hooks.TryGetMemoizedVNode), publicStatic, module.TypeSystem.Boolean);
            tryGet.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(RuntimeMethodHandle))));
            tryGet.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Object)));
            tryGet.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.Int32)));
            tryGet.Parameters.Add(new ParameterDefinition(new ByReferenceType(vnode)));
            tryGet.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_0));
            tryGet.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            var store = new MethodDefinition(nameof(Hooks.StoreMemoizedVNode), publicStatic, module.TypeSystem.Void);
            store.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            store.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Object)));
            store.Parameters.Add(new ParameterDefinition(vnode));
            store.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            hooks.Methods.Add(tryGet);
            hooks.Methods.Add(store);
            module.Types.Add(vnode);
            module.Types.Add(attribute);
            module.Types.Add(hooks);

            var method = new MethodDefinition("Build", publicStatic, vnode);
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            method.CustomAttributes.Add(new CustomAttribute(constructor));
            AddStaticClass(module, "Components").Methods.Add(method);
            return method;
        }

        // GREEN_ON_BASE(characterization): the base widens the short branches of a body before it injects the gate of a Velvet that has no scope API.
        // A branch over a return stays in range once the commit is written ahead of that return. Removing the
        // `SimplifyMacros` that `InjectPlan` calls leaves the short form where the commit has pushed its target
        // out of reach, and the branch no longer finds its target.
        [Test]
        public void Given_AShortBranchOverAReturnInAnAssemblyWhoseVelvetHasNoScopeApi_When_Woven_Then_TheBranchStillReachesItsTarget()
        {
            // Arrange
            var module = NewModule();
            var method = AddAComponentOverAVelvetWithoutScopes(module);
            var il = method.Body.GetILProcessor();
            var target = il.Create(OpCodes.Ldnull);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brtrue_S, target));
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));
            for (var i = 0; i < 120; i++) il.Append(il.Create(OpCodes.Nop));
            il.Append(target);
            il.Append(il.Create(OpCodes.Ret));

            // Act
            using var woven = Weave(module, method);
            var stream = new MemoryStream();
            woven.Module.Write(stream);
            stream.Position = 0;
            using var written = ModuleDefinition.ReadModule(stream);
            var instructions = written.GetType("Synthetic", "Components").Methods.Single().Body.Instructions.ToList();
            var landed = instructions.First(instruction => instruction.OpCode.Code is Code.Brtrue or Code.Brtrue_S).Operand as Instruction;

            // Assert
            Assert.That(instructions.IndexOf(landed!), Is.EqualTo(instructions.FindLastIndex(instruction => instruction.OpCode.Code == Code.Ldnull)));
        }

        #endregion
    }
}
