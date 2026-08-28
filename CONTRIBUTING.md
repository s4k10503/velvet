# Contributing to Velvet

## Project status & contributions

Velvet is built and maintained by a single developer as a personal project. Issues,
discussion, and pull requests are welcome — but reviews and responses are **best-effort**:
they may be slow or sparse, and not every change can be merged.

If you need Velvet to move on your own timeline, **forking is encouraged**. The MIT license
lets you build on it and maintain your own line freely — no need to wait on upstream.

### What a pull request says it came from

A pull request opens by naming its origin. If it closes an issue, the first line closes it on merge:

```
Closes #123.
```

If it closes nothing — a tooling change, a release, something noticed while reading — say so with a
reason, on a line of its own:

```
No issue: found while reading the pool reset helpers.
```

Either answer is fine; the silence is not, and it is the one that happened: a change that came
straight out of an issue was merged without linking it, so the issue stayed open with its work
already shipped. `refuse/pr_body_of_another_branch.py` declines a body that carries neither. An
answer is a closing or referring keyword — Closes, Fixes, Resolves, Refs — against the number right
after it, or the issue's own URL. Closes part of #123 answers as well, for a change that takes half
an issue and leaves the rest of it open. A number on its own is not one: a colour is six digits
behind a `#`, and a number mentioned in passing closes nothing on merge.

The guard reads the description that will be posted, so the body has to exist before the command
runs. What it cannot read it declines rather than skips, and that is the part you are most likely to
meet:

- the file is not there — write the body in a step of its own and open the pull request in the next,
  because a heredoc in the same command has not run yet when the guard looks;
- the path is still unexpanded, or the body comes from stdin;
- the command changes directory and the body path is relative, so `gh` would open a different file
  than this one reads — give the body an absolute path.

A command passing both `--body` and `--body-file` is declined when only one of the two answers:
which of them `gh` posts is not something the guard holds, so pass the one you mean. Where both
answer, or neither does, the pair is judged as one body.

A command carrying no body operand at all — `--fill` and its relatives, `--template`, `--editor`, the
interactive form — holds no text here, so the question goes unasked, and `--dry-run` or `--help`
opens nothing to ask about. `gh pr create` is asked, and so are the `new` alias it also answers to
and `gh pr edit`, whose description reaches the squash message the same way a created one does.
Adding the answer after the fact is still `gh pr edit --body-file`, and a body carrying one passes —
what the guard declines is an edit that leaves the description silent.

It does not judge what the body is about, and no longer tries to. An earlier version dated the body
file against the branch's first commit. Posed the leftover it existed for — a body stamped when one
pull request here was opened, against the branch of the next, whose first commit lands sixteen
seconds later — it allowed it, the window being fifteen minutes wide. Widening or narrowing that
window does not help: a `PreToolUse` hook runs first, so the file it would date is whatever was
already at the path rather than the description that will be posted.

A gh that the parser does not recognise — behind sudo or bash -c, or with gh's own options before the
subcommand — is not seen at all rather than refused. Four attempts to reach it each refused ordinary
commands or broke a sibling guard, so the guard stops where it can answer.

## Local development

1. Install **Unity 6000.3.11f1** (see `ProjectSettings/ProjectVersion.txt`).
2. Open this repository as a Unity project. Velvet is loaded as an embedded package
   from `Packages/com.velvet.core/`; edit it in place.
3. Run the Unity test suites from **Window ▸ General ▸ Test Runner** (EditMode and PlayMode).

### Looking at what Velvet renders

Every `[VelvetPreview]` story can be rendered to a PNG, so a change to layout, styling or paint can
be inspected rather than only measured:

```bash
/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity -runTests -batchmode -projectPath "$PWD" -testPlatform PlayMode -testFilter "Velvet.Tests.StoryCaptureTests" -testResults /tmp/capture.xml -logFile /tmp/capture.log
```

The images land in `Logs/story-captures/` (git-ignored), grouped into a directory per story group, or
under the directory named by `VELVET_STORY_CAPTURE_DIR`. Before each run, the harness deletes every
non-empty path listed in the previous manifest; it does not verify that an entry stays under the output
directory or was created by the harness. Use a dedicated output directory and do not seed or edit its
manifest. The run fails if a story does not mount, if it renders a uniform frame, or — once for the
whole run, not per story — if the bundled stylesheet's `bg-slate-700` probe does not resolve.

**Look at the images.** Those three checks are the floor, not the ceiling: the uniform-frame one in
particular is satisfied by a single differing pixel, so a story can render almost nothing and still
pass. The defects found in this harness so far — a missing backdrop that made every semantic-token
story capture near-blank, and a panel whose height was its content's rather than the story's — were
green under every check and visible immediately in the PNG. Interactively the same stories are live
in **Window ▸ Velvet ▸ Preview**.

### A run that stays in the process list

A process can enter macOS state `UE` — uninterruptible wait while exiting — where no signal
reaches it, `kill -9` included, and only a reboot clears it. Two different situations end there,
and they want opposite responses, so read the log before deciding which one is in front of you:

```bash
ps -eo pid,stat,etime,time,comm | awk '$2 ~ /^UE/'
tail -3 <logfile>
```

**The log ends after `Exiting batchmode successfully now!`** — the run did its work and then could
not exit. Its results stand while the process is still listed. Ten of these were resident while
twenty-five editor invocations ran on the same machine, eight player builds and fifteen EditMode
suites, and all twenty-five completed. Nothing needs doing.

**The log stops mid-compile and stays that size** — the run stalled, and the `netcorerun` it
launched wedges while the editor is still alive, before anything is killed. `SIGTERM` on the editor
reaps the editor; the wedged child does not follow it out, and no signal recovers it.

Not every unreapable process here comes from Unity, and none of them blocks a later run. They
accumulate, so `.claude/hooks/report/wedged_processes.py` reports the set at session start once it
holds enough memory for a reboot to be worth the interruption, and says nothing below that.

### Checking that the tests can fail

A green suite says nothing about whether the tests would have noticed the change. `scripts/test_quality/mutation_check.py`
answers that for the lines a branch touched, by breaking each of them and rerunning the suite:

```bash
python3 scripts/test_quality/mutation_check.py --base main --list   # the mutants, and what they cost, without a run
python3 scripts/test_quality/mutation_check.py --base main
```

`--list` takes the readings that come before an editor is launched — the outstanding-mutation record,
the comment-and-string mask, and which changed code lines an operator reaches — so it refuses where
those would, which is the point of running it first rather than a reason to distrust it. The readings
that need a run, the declarations and the cap among them, it does not take.

[Generators~/README.md ▸ Mutation testing](Packages/com.velvet.core/Generators~/README.md#mutation-testing)
covers this and the generator solution's own run, and owns what the verdicts mean, how to read a
survivor, and which line shapes the operators reach — which is 31% of the changed code lines measured
over the twenty commits ending at `48057c8` with the generator as this branch leaves it, so **a survivor count is a statement about the lines an
operator reached and not about the change**. The run prints both numbers and names the lines it could
not ask about.

**A survivor is closed or answered for; it is not reported and left.** Either the test that should have
noticed gets written, or the line says why nothing could have, above itself:

```csharp
// MUTANT_SURVIVES(equivalent): every caller clamps the operand, so both bounds accept the same set.
```

`equivalent` says the mutated program cannot behave differently; `unreachable` says the state where it
would is not reachable from any entry point. A mutant whose run came back inconclusive counts as
surviving and needs the same answer: an `Assume` that stopped holding measured nothing either. A declaration answers for the change written under it and
is read the three ways the base-red one below is: one over a statement whose mutants all died is stale
and fails, one whose category or reason the script refuses fails, and one the branch did not itself
write answers for the base's change rather than for this one — restate it. It answers for the statement
rather than the line, because a condition broken across two lines carries mutants on both. Only a
whole-suite run over the diff reads any: `--files`, `--filter` and `--assemblies` each ask a narrower
question, and under one nearly everything survives. `--platform` is not one of those — it runs a whole
suite, just a different one — so it reads declarations and writes a receipt, and the platform is part
of the receipt's key so that an EditMode question is never answered by a PlayMode run.

The run also fails on the ways it can measure less than it looks like it measured: a `--max` cap that
left mutants unrun, an editor killed at `--timeout`, a build the compiler or an analyzer stopped, an
assembly the editor never rebuilt, a second editor sharing the machine, and a source whose
comment-and-string mask swallows code, which generates no mutant there and reports nothing.

**What asks whether the campaign was run is a receipt, not attentiveness.** A finished run leaves one
under the campaign's own log directory, keyed on the merge base, the platform and the content of
every file it mutated, and `gh pr create` is refused where no receipt covers the checkout it is run in. A
branch that changes no mutable package source is owed nothing and is not asked; a change no operator
reaches records that verdict and is accepted, since such a branch cannot earn a passing run at all. The
receipt is keyed on what the campaign measured rather than on the head commit, because the campaign
diffs the merge base against the **working tree** — an uncommitted edit to a mutated file changes what
it measured and moves no tree sha — and because 16 of 44 commits over five recent branches changed no
mutable source, each of which would have voided a receipt over a change no operator can see. What it
does not cover is a test-side change: removing a test can make a killed mutant survive, and including
tests would void the receipt on the ordinary act of adding one after the run.

**Merge time is not gated, and cannot be from here.** A guard on `gh pr merge` would read the checkout
the command runs in, which at merge time is `main` after a pull — a tree with no change in it — so it
would pass every merge while printing a verdict about a change it never read. `scripts/pr/settle.py`
merges through the REST merge endpoint besides, which no hook matcher sees. The effective contract is one
campaign at pull-request-open time, and a review round that changes production code after that is
measured by nothing until the next `gh pr create`.

**A round is answered by a layer on top, not by an amend.** A finding cites the commit it was taken
on, so replacing that commit leaves the round and its answer inseparable, and the branch cannot land
without a force-push. `.claude/hooks/refuse/amend_of_published_commit.py` refuses `git commit --amend`
when a `refs/remotes/*` ref reaches HEAD, and when git could not say whether one does. Amending a
commit git placed and found unpushed is the ordinary case, and is what the predicate leaves alone.

**Nothing in CI runs the campaign, and at the measured cost nothing can.** A mutant is one editor launch.
Over the twenty commits ending at `48057c8`, ten generated no mutant at all and the other ten ranged
3 to 51 with a median of 22. A mutant's launch-compile-run measured 100–118 s here against a 94 s
baseline, so a median branch is around 41 minutes and the largest around an hour and a half; on the CI
runner, where the EditMode job alone takes 5m47s, a median branch is 23 sequential Unity jobs.
Run it on a branch before opening the pull request. `Test ▸ test-quality` holds the half that
needs no editor: that the mutants can be generated at all, and that every declaration in the package is one
the script would accept rather than one it silently refuses.

A campaign holds a mutation in the working tree while the suite runs, and records what it holds in
MUTATION_IN_PROGRESS.json at the repository root — untracked, and deliberately not in `.gitignore`, so
`git status` names it beside the file it explains. `SIGINT`, `SIGTERM` and `SIGHUP` put the source back;
a SIGKILL and a machine losing power cannot, so the record outlives them and the next campaign refuses
to start while one is present. Being named in `git status` is not enough on its own — a mutation reads
as an unfinished edit in a file the branch is already touching, and `git add -u` stages it with
everything else — so a commit that would record the held file is refused as well. Put it back by hand
with:

```bash
python3 scripts/test_quality/mutation_check.py --restore
```

A mutation asks whether any test depends on one line. `scripts/test_quality/neuter_check.py` asks the other question a
fixture's name makes — with the mechanism it is named for disabled, does it still pass?

```bash
python3 scripts/test_quality/neuter_check.py --validate   # every anchor still matches exactly once, no editor needed
python3 scripts/test_quality/neuter_check.py
```

Which layer the cut is made at decides the answer. Some vacuous tests are green at the applier cut and red
at the parser cut, because a gate read one layer up survives a neuter one layer down; others are green at
every cut of their mechanism, having no term any of them can move. So a single cut undercounts, and a
fixture is asked only the cuts it reaches, declared in `scripts/test_quality/neuter_cuts.json`. Asking a
parser-only fixture an applier cut reports its whole body as holes, which has happened on two separate
rebuilds of this instrument. `NeuterCutAnchorTests` fails in CI when an anchor stops matching exactly once,
so a rename is caught by the pull request that makes it rather than by the next sweep.

Most holes are legitimate — a negative assertion no cut can falsify, a case whose subject is another cut's
layer — so what is worth catching is not the count but a change to the set, which a hole appearing while
another disappears leaves identical. `scripts/test_quality/neuter_holes.txt` carries the approved set;
`--report` regenerates it, so a sweep is read as a diff against it.

A sweep is one editor run per cut with a source edit between each. `Test ▸ unity-tests` is a single
run with no edit between, and `Test ▸ test-quality` has no editor at all, so what runs there is the
half that needs none:

```bash
python3 scripts/test_quality/neuter_check.py --audit
```

It reads the anchors a sweep cuts at, the hole baseline a sweep's output is diffed against, and
`scripts/test_quality/neuter_uncovered.txt`, which records the parsers and appliers no cut reaches.
Only the anchors are read by a sweep itself — the record by nothing else at all, the baseline only
when `--baseline` is passed — so before this, two of the three were checked by running neither.
Which files those are is two name shapes globbed in `neuter_check.py`, and they are not every
class-driven mechanism — a parser named otherwise, or a manipulator, is answered for by nobody. Within
the two, the record is what an arrival answers to: a class-driven mechanism fails by being ignored,
which reads exactly like a class nobody wrote, so one arriving tomorrow must be given a cut or
recorded as having none.

Each of the three carries a floor, and the audit refuses when one of them reads short. The hole baseline's floor
is on the fixtures it names, because `--report` writes only the fixtures its run swept: aimed at the
record from a narrowed sweep it would replace the rest with them, which is refused before the run
rather than after. A floor catches a collapse, not a thinning — a record rewritten to eighteen
fixtures of one line each still passes.

### Checking that a new test could have failed

Both instruments above ask what happens when the production code is broken on purpose.
`scripts/test_quality/base_red_check.py` asks the cheaper question first, and the one a pull request is
actually claiming: was this case red before the change it says it pins? It checks out the merge base,
copies the branch's changed test files onto it, and runs the cases the branch wrote there.

```bash
python3 scripts/test_quality/base_red_check.py --base origin/main --plan   # what it would ask, no run
python3 scripts/test_quality/base_red_check.py --base origin/main --warm-library Library
```

**It passes only where every changed case was measured on a base tree that demonstrably answers.** Most
of what goes wrong with a run like this ends in a reading nobody took, and a reading nobody took is never
a pass. So a case that passes on the base fails the check, while a surface that only the branch
provides is evidence that the case depends on the change. C# reports that as a compile failure, which
takes its whole assembly down and, where no second round is available, leaves no error list to read:
so a carried C# file is also compared statically, and one spelling a name the base has not got and a
production file the branch changed does have is withdrawn before the run. That reading is a static
approximation of a compile failure, so it does not survive a run that wrote nothing — the platform
goes down with the round, withdrawals included.
Python reports it while loading or running, so the gate accepts it only when static comparison proves
that the named repository file, module, top-level name or callee's keyword parameter is absent on the
base and present on the branch. A module is looked for under the case's own directory and under the
directories that file itself puts on `sys.path` — an insert performed by something it imports is out
of reach, since only the case's own file is read; a top-level name is read off three exceptions rather
than one — the AttributeError of an attribute read, the ImportError of `from module import name`, and
what `mock.patch` raises for a patch by name. An argument *count* is not among them: a call the base
refuses on arity alone reads as a case that could not answer.

A module-level import is answered for case by case, not file by file. `from module import name` is
evaluated once, so a branch-only name in one takes every case of that file down on the base together
— and only the cases that reach the name are read as depending on the branch. A case reaches it in
its own body or its own decorators, at module level, or in the scaffolding of the classes its fixture
is built out of — which includes a shared base class, so long as the file declares it: a base
imported from elsewhere is out of reach, and so are another case's body and another case's
decorators, wherever they sit. Prose does not reach it: a comment
and a docstring are both left out, while an ordinary string is not, because `getattr` names a
surface that way. The rest count against it, because a reading nobody took is never a pass. Every
tolerated case is counted on a line of its own, so a run that measured none of them cannot say so in
silence.

Either reading is only believed on a tree the run proved can build and answer at all, and two things
prove it. Cases of the base's own that the branch did not carry run alongside — C# fixtures for a
platform, Python cases for that lane — and at least one has to pass. A lane with no eligible canary fails closed, as does a run
that wrote no results file at all, rather than reading either as a base that built none of the branch's
tests.

Which cases are the branch's own is decided by comparing each case's code — its own text with the
comments blanked out — against the base's, since a diff over a large rewrite describes untouched text
as re-added, and a comment edited inside a case body is a changed line there. So a change that edits
only comments poses nothing, and the cases kept out are named on a line of their own — `out of scope:
12 case(s) of <file> hold a line this branch changed and no code it changed` — because an empty plan
is equally what a branch that changed no test file at all leaves. Alongside that, the cases the branch
left alone in a file it changed nothing shared in are the base's own text, so one of those going red
means the tree is answering about itself and that fixture's verdicts are withdrawn. Change a
`[SetUp]`, a field, a private helper or anything under `TestUtilities/`, and those cases stop being
the base's text and stop being read as the instrument — the run says which and why.

Red on the base means the base ran the case and the case said no, and only that. Except for a
statically proven branch-only Python surface or a C# compile failure, a case that dies before it
compares and a case the base reported Inconclusive, Skipped, non-runnable or cancelled are reported
as cases **the base could not answer**: no evidence either way, not part of the red count, and a
failed gate. This includes a misspelled Python member, a missing environment file, and a C# fixture
that compiles there but throws while reflecting for private production state the base has not got.

An exception is not that reading on its own. A branch that fixes a crash leaves the base throwing inside
the production code the fix repairs, which is the base disagreeing and stays **red on the base**. The two
are told apart by the first frame of the throw that names a file of this repository — production code, or
the test side — read over the throw the case's own body left. A case carries what its scaffolding threw
as well, so the reading stops at the first section a runner opened: a `[TearDown]`, a `[UnityTearDown]`
or the after half of a test action reaches past what the case asked about, and a `[SetUp]`, a
`[UnitySetUp]` or a before half that threw means the body never ran at all — so a fixture that mounts in
its setup, against a base that crashes there, is not the base disagreeing with the case. A throw that
names no file of this repository keeps the non-answer, so what a crash regression is read as is bounded
by the stack trace its results file carries.

Not every scaffolding throw opens a section in the trace. One carrying a result state of its own replaces
the trace outright — the section marker and the body's own frames together — and arrives under the status
a failed assertion carries with no label beside it. That is the shape a failing log inside a teardown's
scope produces, which is how a fixture whose `[TearDown]` disposes a mount the base crashes in gets here:
Velvet logs a cleanup-path throw rather than rethrowing it, and the runner turns an unexpected error log
into exactly that exception. The section survives at the head of the **message**, which the runner builds
after the replacement. The check reads it only where the trace does not lead back to the case method;
the same words at the head of a body's assertion remain that body's disagreement. Behind a body's own
message it is not read, because a case that disagreed did so whatever its scaffolding went on to do.

Two kinds of case belong on the base, and say so above themselves with a reason a reviewer can weigh:

```csharp
// GREEN_ON_BASE(characterization): the keyed-reorder order this refactor must not change.
```

```python
# GREEN_ON_BASE(refactor): the settle-path names this rename preserves.
```

`characterization` pins behaviour the base already has; `refactor` rides with a change meant to preserve
it. A declaration answers for the change written under it, and it is read three ways so it cannot outlive
what it describes: one over a case that turns out red on the base fails the check, one whose category or
reason the script refuses fails it, and one the branch did not itself write is a declaration for a change
the base already carries and does not cover this one — restate it. A reason may wrap onto the comment
lines under it: the declaration is read from its marker to the end of that block, and a branch that
rewrote any of those lines wrote it. The word floor is measured on the marker's own line rather than
over that span, so the first line has to be a claim in its own right — measured over the span, a
comment line that is not the reason at all counts toward it.
`.claude/hooks/refuse/declaration_first_line_fragment.py` refuses a first line that breaks off on a
word that has to be followed by more of its own clause, on a comma or on a comma and a relativiser, or
on a delimiter it opens and does not close. A first line none of those reach is left alone however the
reason continues under it.

Only the comment block directly above a case is read, so one written above a helper, or with a blank
line between, or left over the case before, silences nothing while looking as though it does. The run
says so on a line of its own — `orphaned: 1 of 3 declaration(s) in <file> sit above no case, so
nothing reads them` — because the alternative is that case failing as green on the base under advice
to write the declaration already above it.

The base tree is a checkout the machine has never imported, and that import is most of what a base run
costs; `--warm-library` copies an existing `Library` into it, sharing blocks where the filesystem will.

`Test ▸ base-red-python` runs the Python lane on every pull request and needs no licence.
`Test ▸ base-red` runs the C# lane where one is configured, but only one round of it: a base that
cannot build one carried file writes no results for anything, and separating that file from the ones
standing next to it takes withdrawing it and asking again, which is what the local run does and a
workflow does not. What the workflow withdraws instead is what the static comparison above proves —
before its round, since after it there is nothing to read. A round that still writes nothing measured
nothing, fails, and prints the local command. Run it locally on a branch whose tests are the point.
`scripts/test_quality/test_base_red_check.py` holds the reader against every test file in this
repository and runs in `Test ▸ test-quality`.

### Trusting the reading itself

Everything above asks what a run measured. `scripts/test_quality/assert_results_from_this_tree.py`
asks the prior question — whether the results are this checkout's reading at all. A results file is
written where the caller points rather than by the run that later reads it, so a run that aborts
leaves the previous one there to be read; the guard refuses a results file no log in the run names,
a log carrying a line rendered as a compiler error whichever analyzer raised it, and a fixture no
source here declares reported under any assembly but one a resolved package owns alone — the last of
these asking nothing of the log at all. Every reading it cannot take is a refusal too, since a guard
exiting 0 unread looks exactly like one that checked. What it cannot separate is two runs writing a
results file of the same name; what answers that is the headless recipe's per-worktree logs
directory, not the guard.

Each Unity job in CI runs it after the suite, and CLAUDE.md's headless recipe runs it beside
`assert_no_inconclusive.py`. `scripts/test_quality/test_assert_results_from_this_tree.py` holds it,
its type reader against every fixture this repository's case reader finds, and its log reading
against every diagnostic identifier the analyzer sources declare, in `Test ▸ test-quality`.

### Checking that a case can report its own failure

An `Assume` that turns out false reports Inconclusive, which the runner does not count as a failure.
Where it gates what the case exists to pin, the day the behaviour breaks is the day the case stops
saying so. `assert_no_inconclusive.py` reddens on one that has fired, which is that day and no
earlier; `scripts/test_quality/assume_gate_check.py` looks for the shape instead:

```bash
python3 scripts/test_quality/assume_gate_check.py
python3 scripts/test_quality/assume_gate_check.py --write-baseline scripts/test_quality/assume_gate_baseline.txt
```

Whether an `Assume` is somebody else's business or a gate on the behaviour turns on whether a
regression can falsify it, which the text does not say. Two sub-shapes fall out of the
`// Arrange` / `// Act` / `// Assert` sections, because those are a statement about which lines are
the behaviour: a gate over a local the Act declared, and a gate sitting in the Assert section. A
comment line names as many sections as it chains, since `// Arrange / Act` and `// Act + Assert` are
how a case says one stretch of code is both. Each reading needs the marker that delimits it, so a
case carrying an `Assume` and missing one is recorded as unreadable — one entry per reading that
could not be taken — rather than passed.
`scripts/test_quality/assume_gate_baseline.txt` carries what is here now, for the reason
`neuter_holes.txt` does — a total nets a fix off against a new one — and both a new entry and one the
scan no longer finds fail the check. It runs in `Test ▸ test-quality` and needs no licence. What to do
about an entry is the fold the fixture conventions above prescribe, and the check prints it. Not every
entry is a defect: the reading is a shape, so an environment precondition reached through a local the
Act declared is in the record too — the panel root's resolved width, read through the element the Act
mounted. Where inspection says an entry is that, fold the other new ones first — `--write-baseline`
rewrites the whole record from the tree and cannot take one entry — then regenerate it, check the lines
it added are the ones being answered for, and say so in the pull request.

**What it does not reach** is a gate above the `// Assert` marker over state the Act changes without
declaring it — a field, or a member of something the Arrange built. That is the shape the rule itself
was written from. Separating it from a legitimate precondition needs to know whether a regression can
falsify the assumption, which the text does not say, and the filters tried instead select so much of
the suite that the record would stop being a list anybody reads. A case carrying only that shape is
not in the record; what still reaches it is `assert_no_inconclusive.py`, on the day it fires.

### Repository scripts

`scripts/` holds the harnesses, grouped by what they are for — `test_quality/` (mutation, neuter,
inconclusive-result and results-provenance guards), `release/` (the release-note builder), `unity/`
(sample sync). Two rules keep the tree readable:

- **Python, named in `snake_case`.** Every harness is importable, so a test can exercise it directly rather
  than only through a shell invocation — which is what `release/test_release_notes.py` does. Python needs no
  runtime or configuration file this repository does not already have; `python3` is on the CI runner and on
  every developer machine that can run the Unity suites.
- **A script's name reaches C#.** `NeuterCutAnchorTests`, `WorkflowTriggerCoverageTests`,
  `StarterSampleShippingTests`, `BaseRedDeclarationTests` and `DocumentationDriftTests` each name one, so
  a rename that misses a reference fails a pull request instead of failing the next person to run it.

The same holds for `.claude/hooks/` and for the two build scripts, so nothing in this repository is
written in shell. That is not a preference about shell: the guards under `.claude/hooks/` parse a
tool call's JSON, compare it against git state and format a refusal, and each of those was already
reaching for `python3` from inside a shell script by the end. What the rule buys is that a guard can
be tested by importing it, and that `Generators~/build` is one file instead of a bash and a
PowerShell copy that nothing compared.

`.claude/hooks/` is grouped by what a script is able to stop:

- `refuse/` — `PreToolUse`. Stops the tool call, by exiting 2 with the reason on stderr or by
  answering with a `permissionDecision` of `deny`.
- `stop/` — `Stop`. Exits 2 to refuse the end of a turn.
- `report/` — `SessionStart`, `SubagentStop`, `PostToolUse`. Stops nothing: `PostToolUse` fires
  after the tool has already run, so it writes into the transcript and exits 0 whatever it finds.
- `lib/` — imported by the rest, wired to no event of its own.

A guard over state that is shared rather than owned by one session — a branch, the stash — belongs
in `.claude/settings.json`, where it runs for every session. An agent's frontmatter can only narrow
that: a guard named there and nowhere else is absent from the main session and from every other
agent type. Such a guard says so with a `HOOK_SCOPE = "session"` line.

A `PreToolUse` guard is reached only for the tools its `"matcher"` names, and acts only on the tools
its own gate admits. It declares the second as a `HOOK_TOOLS = {...}` set and gates on that set
rather than on a literal, which is what leaves the two halves comparable. Register it under the tool
names themselves: a matcher naming no set a check can read is refused — `"*"`, an empty string, and a
`PreToolUse` entry carrying no `"matcher"` key at all, which is the shape the `SessionStart`, `Stop`
and `SubagentStop` entries take. A guard covering several tools spells them out in the matcher.

Every failure mode here is silence, so the wiring is asserted rather than trusted.
`HookWiringCoverageTests` pairs each script against the settings and agent frontmatter that run it,
in both directions, fails on a script name a hook builds a path from that no file answers to, fails
on a `HOOK_SCOPE = "session"` guard that the settings do not register or that an agent registers a
second time, and fails when a `PreToolUse` guard's `HOOK_TOOLS` and its matcher name different tools,
when the guard declares a set its gate never reads, and when its gate answers none of the payloads it
is posed under a tool it is routed — which is the shape an inverted gate takes, since such a gate
returns before its readings for exactly the tools it exists to read. A gate reading no tool name at
all answers under both, and fails the other way round. That first one also fails for a guard none of
the fixture's `GatePayloads` happens to pose anything about — a guard added since that table was last
extended wants a row added to it rather than a change to itself — and for a guard that exits neither
0 nor 2 under any of them, which is one raising rather than deciding.

A guard's readings are the other way it goes quiet. Exiting 0 lets the tool through, so a `gh` that
could not answer and a repository with nothing to report reach the caller as the same event — which
is how an exhausted GitHub GraphQL quota emptied `gh pr view --json headRefName` while the separate
REST quota stayed healthy, leaving `gh` working everywhere else and two merge guards alone silent.
Each guard therefore declares an `UNREADABLE_POLICY` of `"refuse"`, `"allow"` or `"none"`, and an
`UNREADABLE_PROBE` holding a tool input to pose it. `scripts/hooks/test_unreadable_state_check.py`
runs every guard with `git` or `gh` unable to answer and compares the verdict against the
declaration, rather than reading the source for the shape: which direction a `return 0` means is a
property of the call site, and `shared_git_state.py`'s refusal is the answer it gives when git cannot
be asked. `"none"` claims the probe reaches neither program, and fails if either is invoked.
`"allow"` needs a comment above it saying what holds instead, and fails unless another guard in the
directory refuses the same probe — otherwise the tool call is guarded by nothing at all. It runs in
the licence-free `source-generators` job rather than beside the fixtures above, which are skipped
entirely on a checkout with no `UNITY_LICENSE` secret.

A third way is being right about the wrong branch. Two of the preconditions over a merge are asked
of a base — whether the head contains it, and whether it holds an unpublished release — and both
named `main` for as long as `main` was the only branch taking pull requests. So the day a
maintenance line was cut, both refused its release outright and no case disagreed: nothing in the
repository named a second branch at all. The base is the pull request's own field, read through
`.claude/hooks/lib/merge_target.py`, and `scripts/hooks/pull_request_base_check.py` poses every
guard in the directory a pull request based on a branch that is not `main`, in a repository where
`main` holds both of those things and the named base holds neither. A guard that judges either of
them against `main` fails it without anybody having remembered to write a case.

The `Stop` guards declare the same policy and are held to one thing more, because blocking was never
what they got wrong. They blocked, and described the pull requests rather than the reading — so the
deferral the message invited named the API error instead of whatever the work was waiting on.
`.claude/hooks/lib/repository.py` owns both halves of the remedy: a second way of asking, drawn on a
different quota, before blindness is declared at all, and the sentence a block has to carry when it
is. The same check runs every guard in `.claude/hooks/stop` and requires that sentence of one that
blocks. It poses two modes: nothing answers, and — the one a second way of asking creates — the
listing answers while every per-pull-request read fails, which is where a guard can report on a
pull request it learned nothing about. An empty answer is posed as neither, being the ordinary state
`open_backlog.py` acts on. A guard whose own question is answered in a mode that broke somebody
else's reading declares `UNREADABLE_ALLOWS`, with a comment and with a sibling that refuses there,
so an exemption no other guard stands behind is reported rather than taken.

### Source generators

The Roslyn source generators live under `Packages/com.velvet.core/Generators~/` and target a
.NET SDK pinned by `Generators~/global.json`.

```bash
cd Packages/com.velvet.core/Generators~
dotnet test Velvet.SourceGenerators.sln -c Release   # run generator unit tests
```

The compiled DLLs under `Packages/com.velvet.core/Runtime/Plugins/` are committed, so a change to
generator source is only complete once they are rebuilt and committed too. Build and DLL-shipping
steps live in [Generators~/README.md](Packages/com.velvet.core/Generators~/README.md) — `./build.py`
on every platform.

## Continuous integration

| Workflow | Trigger | Unity license | Required to merge |
|----------|---------|---------------|-------------------|
| `Source generators ▸ source-generators` | push (filtered) / every PR / merge group | not required | no |
| `Source generators ▸ repository-settings` | push (filtered) / every PR / merge group; skipped when the workflow runs in a fork | not required | no |
| `Source generators ▸ Required checks (generators)` | push (filtered) / every PR / merge group | not required | **yes** |
| `Test ▸ license-check` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ unity-tests` (EditMode / PlayMode) | push (filtered) / every PR / merge group | **required** (skipped if absent) | no |
| `Test ▸ release-notes` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ publication` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ test-quality` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ base-red-python` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ base-red` (EditMode / PlayMode) | every PR | **required** (skipped if absent) | no |
| `Test ▸ Required checks (Unity)` | push (filtered) / every PR / merge group | not required | **yes** |
| `UPM ▸ split` | push to `main` / manual (`workflow_dispatch`, which also tags and publishes the release) | not required | no |
| `Docs` (DocFX → GitHub Pages) | push (filtered) / release / manual | **required** (skipped if absent) | no |

The two required checks are aggregates, and the real jobs are not required themselves. A required check
that does not run stays `Pending` and blocks the pull request with nothing able to clear it, which is what
a trigger filter, a matrix change or a rename would each cause. The aggregates carry no trigger filter, `needs:`
the real jobs, and pass when every dependency is `success` **or** `skipped` — the second is what lets a
fork with no `UNITY_LICENSE` merge, since `unity-tests` is skipped in exactly that case.

In `test.yml` and `generators.yml`, filtering therefore applies to `push` only, by branch as much as by path — so a pull
request runs both workflows whether it is based on `main` or on a maintenance branch, and so does every
merge-group entry once a queue is turned on. The `merge_group:` keys are there for that, and
`WorkflowTriggerCoverageTests` fails if either of the two gated triggers goes missing from either
workflow, or gains a child key whose colon follows its name under one of them. Skipping work per queue entry is a job-level condition, not a
trigger filter: a required check that does not start has nothing able to clear it.

`main` does not require heads to be up to date before merging. That setting serialises the queue — each
merge invalidates every other branch's run, and the Unity matrix is 21–25 minutes — without testing the
combination it exists to protect any better than a merge does. What does test that combination is a merge
queue, which is a separate change.

The source-generator tests and the `upm`-branch split run with no Unity license, so the pipeline
works out of the box on a free account. The Unity EditMode/PlayMode job is skipped automatically
unless a license secret is configured.

### Enabling Unity tests (free Personal license)

A free Unity **Personal** license works in CI. Add these **Actions secrets** (Settings ▸ Secrets and
variables ▸ Actions):

- `UNITY_LICENSE` — the contents of your `.ulf` license file
- `UNITY_EMAIL` — your Unity account email
- `UNITY_PASSWORD` — your Unity account password

Get the `.ulf` by activating a free Personal license in Unity Hub and copying the generated file
(`Unity_lic.ulf` — macOS `/Library/Application Support/Unity/`, Windows `C:\ProgramData\Unity\`,
Linux `~/.local/share/unity3d/Unity/`). CI runs Unity through game-ci, which randomizes the machine
ID on activation, so a locally-activated `.ulf` works. If activation ever fails with
"Machine bindings don't match", generate a CI-clean `.ulf` instead: create an activation file
(`.alf`) and upload it at <https://license.unity3d.com/manual>.

The same secret enables both the Unity EditMode/PlayMode tests (`test.yml`) and the API-docs build
(`docs.yml`). A Personal license has a single seat, so avoid running CI while the local editor holds
the license on the same account.

## Releasing

The CHANGELOG holds two open sections, and which one an entry goes in is settled when the entry is
written rather than when the version closes. `## [Unreleased]` is what a minor or a patch may ship.
`## [Unreleased — breaking]` is what has to wait for a major: an API a caller has to edit around, and
behaviour a working application would notice changing.

1. Close the version in `Packages/com.velvet.core/CHANGELOG.md` and bump `version` in
   `package.json` to match. A major moves the breaking entries up into `## [Unreleased]` and leaves
   their heading standing with none. A minor or a patch closes only over a section already empty,
   and `published_check.py` refuses one that is not: a release publishes the tree rather than the
   section, and `main` was found carrying the code an entry described while that entry still
   waited. A line that never took those changes has an empty section, which is what keeps the
   reading silent on the maintenance line. Rename
   `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` **last**, once nothing further is going into it:
   `changelog_into_closed_version.py` refuses a write into a dated section, and the rename is what
   dates it. The breaking heading itself is never dated and never deleted, and a `**Breaking:**`
   bullet in `### Highlights` belongs to a major and to no other release; `test_release_notes.py`
   refuses each of those. Where the entries went is read from the change rather than the file, by
   `published_check.py` on the pull request that closes the version: a major has to close with the
   section empty and every entry of it word for word in the version being closed, and a minor or a
   patch may neither take anything out of it nor leave anything in. So a wording change belongs in a
   change that closes no version — which is also how an entry is reclassified out of the section,
   deciding it was never breaking, and none of this is asked of one. A major also answers for the
   breaking work still in flight: an entry written for it sits on its own branch until that branch
   merges, so the CHANGELOG holds only the part that has landed. Name every open pull request adding
   to `## [Unreleased — breaking]` and say which the version carries —
   `breaking_in_flight_check.py` refuses one that names none of them, and "not this one" is a
   decision it accepts. Give the version a row in `SECURITY.md`'s supported-versions table, and
   decide there what happens to the series it succeeds: `supported_versions_check.py` refuses a
   release the table does not cover with one row marked supported.
2. Merge to `main` (the `upm` branch is updated automatically).
3. Run the **UPM** workflow via *Actions ▸ UPM ▸ Run workflow*, entering the same version.
   This tags `vX.Y.Z` on the `upm` (package-at-root) commit and publishes a GitHub release.

Between step 2 and step 3, `main` names a version that does not exist. The dispatch builds the note
from the CHANGELOG section, which was written before anything merged in that window and describes none
of it — so those commits ship inside the release, undescribed. v2.0.1 spent a day there and took twelve
merges, and publishing it meant tagging the release commit by hand and dispatching from the tag, since
dispatching from the branch would have shipped all twelve.

So the window is guarded: `settle.py merge` and `gh pr merge` refuse a pull request based on the
branch whose window is open, and `Test ▸ publication` fails for one whose checks run in it.
`scripts/release/published_check.py` decides it and states the repair in its own message.

**A pull request that went green *before* the release landed keeps that result.** This repository sets
`strict_required_status_checks_policy: false` so a 21-minute Unity matrix is not re-run for every base
move, so the merge button on github.com stays enabled for it. What refuses there is `merge_onto_unpublished_release.py`, `stale_merge.py`
and `settle.py`'s contains-base precondition, none of which github.com consults; turning the strict
policy on is what would close it server-side, at the cost that buys.

**A green pull request left sitting starts refusing every edit.**
`.claude/hooks/refuse/edit_while_a_ready_pr_sits.py` refuses every editing tool once one has been ready
for fifteen minutes, and the instruction it prints is `settle.py merge`. Ready is that command's own
decision rather than a second reading beside it, so a pull request this window declines is not
recorded ready while the window can be read. It is read by
`published_check.unpublished_reason`, which answers None on any git failure, so an `ls-remote` that
times out mid-poll records the green ones ready again and the stall is back for as long as that
lasts. The deferral the hook's own message describes is still what clears it.

**If the dispatch itself is what is broken, the guard has no in-band escape.** `upm.yml` runs from the
workflow file at whatever ref is dispatched, so tagging the release commit and dispatching from the tag
re-runs the same broken workflow — that manoeuvre solves a different problem, a branch tip that has
moved on, which is what v2.0.1 needed. What works is a branch carrying the fix on top of the release
commit, pushed and dispatched with `--ref`, accepting that the fix ships inside that release. What does
not is an administrator merge: `protect-main` lists no bypass actor and reports
`current_user_can_bypass: never`, so `gh pr merge --admin` is refused like any other. Short of the
branch, the remaining lever is the ruleset itself — a bypass actor, or `enforcement: disabled` — which
is a repository-settings change and not a merge.

Note what a dispatch from a tag costs either way: `upm.yml` force-pushes the split to `upm`, so a
consumer tracking `#upm` unpinned drops back to that commit until the next push to `main`.

**Afterwards, a red left over from the window does not clear itself.** The tag list is read live, so any
fresh run of the check passes once the release exists — re-run the failed jobs, or push.

The release notes are built from that CHANGELOG section by `scripts/release/release_notes.py`, so a release
is never written twice. Each version therefore needs a `### Highlights` block above its
`### Added` / `### Changed` / `### Fixed` headings: a handful of one-paragraph bullets, which the
note leads with and the long-form entries follow, collapsed. A version missing one fails the release
— and fails the `Test ▸ release-notes` check on the pull request that introduced it, which is where
you want to hear about it. Preview a note before merging:

```bash
python3 scripts/release/release_notes.py --version X.Y.Z --repo s4k10503/velvet
```

Consumers then install a pinned version with:

```jsonc
"com.velvet.core": "https://github.com/s4k10503/velvet.git#vX.Y.Z"
```

The shape rather than a version, because a document naming one is right on the day it is written and
wrong from the next release: `scripts/release/pin_example_check.py` refuses one inside a `.git` URL's
fragment, in the tracked markdown and workflow files, and runs in `Test ▸ release-notes`.

### The maintenance line

A maintenance branch is named `<major>.x` and cut from that series' last release; `main` is where the
next major is built. **One line is maintained at a time — the series immediately before `main`'s.**

**The line takes fixes, and the CI its own pull requests need. It does not take features.**

**The line is merged forward into `main`, not picked back out of it**, and a fix that lands there is
owed a release. The first is not optional: a fix that stays on the line is one a branch cut from
`main` reproduces in full — #732 removed a `branches:` filter `main` still carried, and stayed for
three days. Merging forward is also what makes it checkable, because git then records the ancestry
and `merge-base --is-ancestor` answers it without anyone reading a pull request;
`unreleased_maintenance_line.py` reports both at session start.

**A fix that lands there is owed a release.** `main` between releases is expected to hold unreleased
entries; a maintenance line holding them is a backport nobody shipped, and the release readings do
not see it — `published_check.py` asks whether a *closed* version went unpublished, and these entries
belong to no version yet. `unreleased_maintenance_line.py` reports the state at session start; it
found the line holding one entry for four days, which no session had noticed.

**A commit that carries a breaking change does not come, even when it also carries a fix.** What makes
one unsplittable is the code: the fix can name a symbol that arrives with the breaking half, so the
pick auto-merges and then does not compile. If the fix is wanted on the line it is written fresh there
— and the line is merged forward into `main`, which the session-start report above names when it has
not been. `main` may already carry its own; ask before
opening one.

**Decide per commit, not per `[Unreleased]` entry.** The mapping runs many-to-many, and a commit may
write no bullet at all. Where one did, ask which section its bullets sit in on `main` today — reading
the nearest heading out of a diff's context answers the wrong question.

**Cherry-pick with `-x`, in the order the commits landed, and compile after each one.** A clean
cherry-pick is not evidence the tree still builds: a pick can name a helper that arrives with a commit
that stayed behind. Nor is the absence of a conflict evidence the pick is right — where a change both
adds and removes and the line has nothing to remove, the merge keeps the addition and compiles.

**Take the CHANGELOG hunk out of the pick and write the entry on the line by hand.** Picked as it
stands it applies clean and lands in the *released* section, and reopening `## [Unreleased]` does not
attract it. `changelog_into_closed_version.py`, which step 1 above relies on, is registered against
`Edit|Write`, so a cherry-pick does not reach it — and the line does not carry that hook at all.

**The record is the `-x` trailer.** Squash as everywhere else, and put the `(cherry picked from commit
…)` lines **in the pull request body**. Do not reach for
`git cherry`: measured here, it reported every commit it walked as absent from the line, including the
ones whose fix the line carries.

A new line needs no change to the merge and release guards — they judge the pull request rather than a
branch name, and `### Repository scripts` above says how that is held. What a cut does cost is
everything else written for one branch:

- each required workflow's `pull_request` trigger must filter by no branch, or a pull request based on
  the line starts neither workflow — and since no ruleset covers the line either, it reads as one that
  had nothing to run and merges with no evidence behind it;
- `.github/dependabot.yml`'s `target-branch`, which names the line rather than `main`, so a new one
  needs its own entry;
- the `protect-main` ruleset, which covers `main` and nothing else, so a new line starts unprotected —
  and `generators.yml` asserts that ruleset's ref list is exactly `main`, so widening it to reach the
  line reds every pull request in the repository until that assertion moves too;
- `upm.yml`, whose force-push of the split and whose repo-wide `PREV_MAIN_TAG` both assume a single
  series is publishing.

A line inherits neither the scripts nor the hooks `main` grew after it was cut. Run a script from `main`'s copy: `assert_results_from_this_tree.py` with
`--project` at a checkout the suite has run in, `base_red_check.py` with `--base origin/<line>` besides
since its default is `origin/main`, and `assume_gate_check.py` with an absolute `--baseline`, which is
what escapes `--project`, since the line carries no record of its own — that comparison is cross-tree,
so it reds on the line's own un-folded gates as well as on anything the backport adds. A hook has no such choice: it runs from whichever tree `CLAUDE_PROJECT_DIR` names, so a worktree
rooted on the line runs the line's copies, and those predate the fix that made a merge guard read the
pull request's own base.

## API documentation

The API reference under `docs/` is generated by [DocFX](https://dotnet.github.io/docfx/) from the
runtime XML doc comments. `docs/Velvet.Docs.csproj` compiles the `Velvet` sources only so DocFX can
bind the public API — it is not part of the Unity build and ships nothing.

Generate the site locally:

```bash
# 1. Compile the project once (so Library/ScriptAssemblies has the referenced DLLs):
/path/to/Unity -batchmode -nographics -quit -projectPath . -logFile -
# 2. Install DocFX and build:
dotnet tool install -g docfx     # once; ensure ~/.dotnet/tools is on PATH
docs/build.py                     # → docs/_site/index.html
```

For a non-default editor install, set `UnityEditorContents` (see `docs/build.py`). The
**Docs** workflow publishes the site to GitHub Pages on release (it needs the Unity license secret;
verify its editor-image tag and managed-assembly path on the first run).
