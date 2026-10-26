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

1. Install **Unity 6000.3.23f1** (see `ProjectSettings/ProjectVersion.txt`).
2. Open this repository as a Unity project. Velvet is loaded as an embedded package
   from `Packages/com.velvet.core/`; edit it in place.
3. Run the Unity test suites from **Window ▸ General ▸ Test Runner** (EditMode and PlayMode).

### Looking at what Velvet renders

Every `[VelvetPreview]` story can be rendered to a PNG, so a change to layout, styling or paint can
be inspected rather than only measured:

```bash
mkdir -p Logs && R=$(mktemp -d "$PWD/Logs/capture.XXXXXX") && echo "$R"
VELVET_STORY_CAPTURE_DIR="$R" /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -runTests -batchmode -debugCodeOptimization -projectPath "$PWD" -testPlatform PlayMode -testFilter "Velvet.Tests.StoryCaptureTests" -testResults "$R/capture.xml" -logFile "$R/capture.log"
```

The images land under the directory named by `VELVET_STORY_CAPTURE_DIR` — the run's own, above —
grouped into a directory per story group, and in `Logs/story-captures/` (git-ignored) when it names
none. Before each run, the harness deletes every non-empty path listed in the previous manifest; it
does not verify that an entry stays under the output directory or was created by the harness. Use a
dedicated output directory and do not seed or edit its manifest. The run fails if a story does not
mount, if it renders a uniform frame, or — once for the whole run, not per story — if the bundled
stylesheet's `bg-slate-700` probe does not resolve.

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

[Generators~/README.md ▸ The Unity assemblies](Packages/com.velvet.core/Generators~/README.md#the-unity-assemblies)
covers this and the generator solution's own run, and owns what the verdicts mean, how to read a
survivor, and which line shapes the operators reach and what share of a diff that is. The share is a
minority, so **a survivor count is a statement about the lines an operator reached and not about the
change**. The run prints both numbers and names the lines it could
not ask about.

**A survivor is closed or answered for; it is not reported and left.** Either the test that should have
noticed gets written, or the line says why nothing could have, above itself:

```csharp
// MUTANT_SURVIVES(equivalent): every caller clamps the operand, so both bounds accept the same set.
```

`equivalent` says the mutated program cannot behave differently; `unreachable` says the state where it
would is not reachable from any entry point, and neither category answers a mutant the verdict calls **uncovered** merely because no case of the EditMode suite ran its site: that is code with no test, and the answer is the test, a declaration being right only where the state really is unreachable or the mutation really equivalent. A mutant whose run came back inconclusive counts as
surviving and needs the same answer: an `Assume` that stopped holding measured nothing either. A declaration answers for the change written under it and
is read the three ways the base-red one below is: one over a statement whose mutants all died is stale
and fails, one whose category or reason the script refuses fails, and one the branch did not itself
write answers for the base's change rather than for this one — restate it. It answers for the statement
rather than the line, because a condition broken across two lines carries mutants on both.

A statement carries more than one mutant — `if (depth > cap) Reset();` has a boundary flip and a
void-call removal — and the form above answers for every one of them. Where only one survives today
that is unambiguous now and widens the moment a sibling starts surviving, with nothing to report it:
staleness asks whether the statement still produces a survivor, and the one already answered for
keeps it non-stale. Name the operator to answer for one:

```csharp
// MUTANT_SURVIVES(unreachable, line removed): nothing pairs the depth cap with a transition.
```

A named declaration answers for that operator alone, and is stale when that operator's mutant stops
surviving whatever its siblings do. The unnamed form is unchanged and still reads the statement
whole, which is what a statement carrying one mutant wants. Only a
whole-suite run over the diff reads any: `--files`, `--filter` and `--assemblies` each ask a narrower
question, and under one nearly everything survives. `--platform` is not one of those — it runs a whole
suite, just a different one — so it reads declarations, and the platform is part of the verdict
records' key so that a kill a PlayMode run took is never kept for an EditMode one. `--survivors-of`
takes a run on one platform as the first of two passes: the run on the other platform measures only
that run's survivors and decides over both, which is the decision CI takes. `--survivors-shard K/N`
narrows what is read to the mutants the first run's shard K of N was dealt, which is how a shard's job
measures the survivors of its own mutants on the other platform.

**A changed line is asked about once.** A line can generate several mutants — `a <= b && c` has a
boundary flip, a logic flip and a clause removal — and the campaign keeps one of them, so its cost
grows with the lines a branch changed rather than with how many operators each of them admits.
That is a decision about cost, motivated by Google's "Practical Mutation Testing at Scale"; none of
its figures is claimed to hold for this repository. The mutant kept is the first of these that the line generates, and where one operator
generates several, the leftmost:

`boundary`, `equality`, `arithmetic`, `clause removed`, `logic`, `literal`, `guard removed`,
`line removed`

The order puts first the mutant that changes least about what the line does. A test is least likely
to notice it, so a line whose first mutant dies is taken to have been noticed, and one whose first
mutant survives is the question worth asking. The order is a decision and not a measurement:
`mutation_check.LINE_PRIORITY` holds it, `OnePerLineTests` pins it, and a different order is a change
to that tuple together with this paragraph. The choice depends on the mutants a line generates and
on nothing else, so every shard and every later run reads the same list.

What the cap leaves out is not measured, and a survivor count is a statement about the mutants kept.
`--list` prints the kept ones. A declaration that names an operator the cap dropped from its line
(a survival declaration naming the logic operator over a line whose boundary flip was kept) is not stale when the
kept mutant dies or is absent, because the mutant it described is no longer generated and there is
nothing to measure it against; an unnamed declaration is not stale over a line with dropped mutants
either. It does not answer a kept mutant of another operator that survives: that survivor stays
unanswered ("answers for 'logic', not for this") until the line is written so that its own
operator is the one kept — split the expression over lines. A declaration naming an operator the
line never carried is refused.

A kept mutant the compiler rejects as no program is replaced by the next dropped mutant of its line
in the same order, until one compiles or none remain; the line is then reported as rejected, as
before. Only an arithmetic `+` rewritten to `-` over operands that turn out to be strings is ever
rejected that way, so that is the only case the fallback has. The verdict record stays the kept
mutant's, which is what the plan's list names, and carries the operator that stood; a declaration
then answers for that operator. Every shard regenerates the dropped mutants from the tree, so the
fallback needs nothing beyond the shared list.

The run also fails or stops rather than pass over a mutant nobody asked about, and
[Generators~/README.md ▸ The Unity assemblies](Packages/com.velvet.core/Generators~/README.md#the-unity-assemblies)
says when it does which.

**The campaign keeps a record of the default branch, and no pull request waits on it.** It takes one half of
[cargo-mutants' CI guide](https://github.com/sourcefrog/cargo-mutants/blob/main/book/src/ci.md), which
checks the code a pull request changed and every mutant on the development branch: this repository
measures the development branch and does not check a pull request's change. It also follows
[StrykerJS's incremental mode](https://github.com/stryker-mutator/stryker-js/blob/master/docs/incremental.md),
which keeps each mutant's result and measures it again only when what decided it changed;
[PIT's](https://github.com/hcoles/pitest) `--historyInputLocation` and `--historyOutputLocation` are the
same idea in a third tool. Its runs measure what is not known yet and keep what they measured, and
nothing is measured again while what decided it stands. No pull request starts the campaign: a change
meets it on the default branch, and a survivor it leaves is answered afterwards. The `Mutation campaign`
workflow runs:

- on every push to the default branch, nightly, and on a dispatch with no `base`
  (`gh workflow run mutation.yml --ref <branch>`): a **record run** over the campaign's scope, below,
  which writes what it measured into the record;
- on a dispatch with a `base` (`gh workflow run mutation.yml --ref <branch> -f base=main`): the branch's
  change against its merge base with `base`, as a local run reads it, written into the record the same way.

No branch-protection rule requires any of them, and [Merging a pull request](#merging-a-pull-request)
says how a merge leaves the campaign's runs on its head out. What the record decides is a release:
[Releasing](#releasing) owns the gate that reads it.

**A record run's scope is everything since the later of the cutover and the previous release.**
`scripts/ci/change_base.py`'s `campaign_scope` names the base a record run reads its change against:
the later of the newest `v*-main` tag in the head's history and the cutover, the oldest first-parent
commit that added `scripts/ci/mutation_record.py`. Everything merged before the cutover passed the
per-pull-request campaign the record replaced, so nothing before it is measured, and nothing is
backfilled. `ScopeTests` holds both orders and the history with no cutover.

**The record is a branch of one file per verdict.** The `mutation-record` branch holds an entry for each
mutant a run decided, at `records/<key>.json`, where the key hashes the mutant (file, line, column,
operator and both texts), the mutated file's bytes, and `MUTATOR_VERSION`. A run fetches the
branch; its `record` job commits what the verdict wrote through the Git Data API as the workflow's
`GITHUB_TOKEN`, moving the branch only forward and retrying over a newer branch when another run moved
it first (`scripts/ci/mutation_record.py`). Entries under different keys never touch, so no two runs
conflict, and the same key written again is the newer verdict. The branch shares no history with the
code and nothing merges it.

**Only commits GitHub signed and attributed to the workflow's bot are read.** No ruleset restricts who
writes the branch, and `GITHUB_TOKEN` has no administration permission to set one, so the writer is
held by what each commit carries. The `record` job's commits give no author, committer or signature of
their own; the commits API reported the first one as authored by `github-actions[bot]`, committed by
`web-flow` and verified with reason `valid`. Every read — each run's plan and the release gate — walks
the branch's first-parent history and asks the commits API about each commit; one not reported
`verified` with reason `valid`, authored by `github-actions[bot]` and committed by `web-flow` refuses
the whole record, naming it. The limit: this refuses an unsigned push, a commit authored by a named
person, and a commit committed by anyone but `web-flow`. It does not tell this workflow from another
one that writes the same way, nor from a commit that someone with write access has `web-flow` sign
with the bot named as author. An administrator can still rewrite the branch. A refused record stops
answering until the refused commits are gone. A mutant measured to nothing — no shard recorded it, the project lock or the build
system kept its suite from running, its assembly was never rebuilt — writes no entry, so the next run
measures it.

**`MUTATOR_VERSION` is the tool's part of the key, and only the mutator moves it.** It stands for what
decides which mutants a tree has and how each is written into its file: the `Mutant` class and
`mutation_check.py` from `CHARACTER_LITERAL_REACH` through `apply_mutation` — the reading of C# that
places a mutant, the operator tables, the one-per-line cap and the rewrite. Any other edit to the
tooling keeps every entry, where a digest of the whole tool in the key would re-key the scope on every
edit to `mutation_check.py`. `MutatorVersionTests` pins a
digest of that code to each version and fails when the code moves without the version being raised
and the new digest pinned beside it. An edit elsewhere that changes what a verdict means raises the
version by hand, which measures everything again.

**An entry is reused by StrykerJS's rule**, which reuses a kill while the test that killed it still
exists unchanged, and any other result while no new test covers it and no test changed. Here an
entry is found only for the same mutant of the same file bytes under the same `MUTATOR_VERSION`, and
then:

- a kill traced to its killing cases stands while every one it recorded — up to
  `MAX_RECORDED_KILLERS` of its behavioural ones, the first by name, each with the hash of every source
  file that declares the case's fixture or one it derives from, and of every test source declaring a
  type, outside any other type, or an extension method that one of those spells in its code, followed
  through such sources in turn — is still declared in the tree and no code in the files recorded for it
  has changed. Comments are left out of each hash. What a killing case reads at run time is not
  followed: a fixture that reads every loaded assembly is a test file like any other;
- any other verdict — a survivor, an uncovered mutant, a timed-out launch, a crash, a compile error,
  and a kill traced to no case — stands while no test source changed: the entry carries
  `tests_digest`, a hash over every test source (every file under a `Tests/` directory and under
  `Packages/com.velvet.core/TestUtilities/`, a C# one without its comments), and a change to any of
  them measures it again.

Every verdict the record keeps (`RECORDED`) is one this rule accepts over the tree that wrote it, which
`ReuseDecisionTests` holds verdict by verdict: an entry nothing could accept would leave its mutant
unresolved for good, holding every release and dealt to every run. A compile error is
final there, as StrykerJS scores it as no result (`RECORDED_FINAL`): the run that measures one names it among the mutants nothing was asked about, and
once it is recorded neither a later run nor the release gate fails on it.

**What the rule does not see**, as StrykerJS says of its own: an edit to a production file other than
the mutated one, a stylesheet, an assembly definition or a project setting does not lapse a verdict,
though it can change what a mutant does. The per-change key this replaced hashed every such input, and
on the default branch it lapsed on 36 of the 56 first-parent commits of one week, measured, which would
have measured the scope again nearly every day. Where such a reach is suspected, deleting the entries
from the branch or raising `MUTATOR_VERSION`, which moves every key, measures them again. The
other side of the rule is that a change to any test re-measures every survivor in scope, the declared
ones included.

**One run measures at most a cap, in a fixed order, and the next run continues.** `mutation-plan`
generates the scope's mutants in the order `--list` prints them, looks each one up in the record, and
deals the first `RECORD_CAP` the record does not answer across at most `RECORD_SHARDS` shards
(`mutation_check.py --cap`, `--max-shards`); the rest are pending, left to a later run, and fail nothing.
`mutation.yml` declares both figures: 240 mutants over forty-eight shards of five, which `mutation-shard` runs at most
twelve at a time (`max-parallel`, below), so a full run's shards are four waves of the shard job's 360-minute
limit, 24 hours at the very worst, with the plan, Library, verdict, record and report jobs on top;
a scheduled run that comes meanwhile waits for it in the concurrency group below. `RecordBudgetTests`
holds the cap inside its shards' ceiling, the shard waves inside 24 hours and the shard count at the most they hold. Where a run left mutants pending or a shard's budget left some unmeasured (below), recorded at least one entry, and every shard passed, its
`record` job dispatches the next run onto the same ref, which deals the next mutants in the same order,
since the ones just answered are in the record now. A run that recorded nothing dispatches nothing, so a
mutant no run can measure stops the chain instead of being dealt to it forever; the next push or night
deals it again. Record runs on one ref share a concurrency group that cancels nothing, so no two of
them measure at once, and GitHub keeps only the newest waiting run, which reads the newest head.

**A shard stops before its step does.** Each measuring step passes the campaign `--budget`, its timeout less
ten minutes, counted from the campaign's start; `BudgetWorkflowTests` holds the difference above the
longest measured time before a baseline and after a last verdict. Before a mutant is started, the bounds of the launches it may take
(`mutant_bound` for the whole suite, the area's bound for a narrowed launch ahead of it, two whole-suite
bounds for a survivor read by the fixtures that read IL) and `BUDGET_MARGIN` are held against what is left,
not against what a mutant usually costs: one that hangs takes every bound it is given. One that does not
fit reads `not measured (budget)`, is recorded as such, and the shard exits 0, so the step passes, the shard's
other verdicts are uploaded and the record job can dispatch the next run. The run's decision fails on such
a mutant as on any unmeasured one, nothing records it, and the next run measures it again. A session's launch
is bounded by what is left as well. Before the budget, a shard whose mutants took longer than the step reached
the step's own timeout, which fails the shard and so keeps the record job from dispatching the next run.

**The same tree and the same record give the same answer.** Which mutants a run deals, which entries it
reuses, and the release gate's mutation half are functions of the git history and the record branch
alone: no clock, no run history, no artifact and no issue is read. The gate's other half reads the
required workflows' push runs, which a re-run moves. A mutant the record does not answer, never
measured or measured to nothing, is never a pass. `RecordGateTests`, `CappedPlanTests`,
`ReuseDecisionTests`, `ScopeTests` and `test_mutation_gate.py` each take their decision twice and compare.

`Mutation campaign ▸ mutation-plan` generates the mutants of the change, taking the readings `--list`
takes, and stops there when there are none — because no mutable package source changed, or because
no operator reaches the lines that did. The second passes, where a local run refuses it: its job
summary names the lines. The cap keeps what a run deals inside its shards' ceiling; without a licence
no shard runs, and the plan passes as the Unity jobs skip.
Otherwise,
where a licence is configured, the mutants are measured in two passes, because a mutant is killed
when either suite fails on it, and one only a PlayMode fixture notices survives the EditMode suite.
`Mutation campaign ▸ library` restores the EditMode Library the branch or the default branch holds,
imports the head once where neither holds one under its exact key, and saves that for the branch, because
`Test ▸ unity-tests` saves its own under a pull request's ref, which this run's is not.
`Mutation campaign ▸ mutation-shard` then measures every mutant dealt in up to the run's shard count of jobs, each running the mutants it is dealt
(by expected cost, below) in the editor image `Test ▸ unity-tests` pulls and recording its verdicts — against its area's own
test assemblies first where those are cheap, and against the whole EditMode suite wherever that did
not kill it, which
[Generators~/README.md ▸ The Unity assemblies](Packages/com.velvet.core/Generators~/README.md#the-unity-assemblies)
owns. When a job's EditMode pass has ended, the same job counts the mutants it left surviving and
measures only those against the whole PlayMode suite, on the same runner and licence: the PlayMode pass
starts as soon as its own shard's EditMode pass ends, rather than after the slowest shard's, and adds no
job. An account's concurrent hosted jobs are few, and a job added for the PlayMode pass would queue
behind the EditMode shards and behind the jobs of whatever else runs. That pass has a ceiling of its
own, `SHARD_CEILING["PlayMode"]` survivors in one shard, and over it the job fails after uploading its
EditMode records: EditMode tests for the survivors bring the count under it, since no PlayMode test
lowers it. A PlayMode pass runs only where its job's EditMode
pass passed. The job's 360 minutes, the most a hosted job runs, hold the EditMode step's 180, the PlayMode step's 165
and 15 for the checkout, restore and uploads around them; `ShardCeilingTests` holds each step to its platform's ceiling.
`Mutation campaign ▸ mutation-verdict` reads both passes' records and the entries the plan reused, and decides as a local run over the same diff decides: an unanswered survivor, a stale declaration or
a mutant nothing measured fails it, and a survivor of the EditMode pass that no PlayMode shard
recorded is one nothing measured; a mutant past the cap fails nothing. Unlike a local run it takes every
mutant dealt rather than stopping at `--max`. A reused entry is read against the verdict job's own tree
rather than the plan's file, and one that no longer stands there is
`not measured (the recorded verdict no longer stands)`, so a reuse never turns a survivor into a pass.
The verdict writes an entry for every mutant it measured to a verdict (`--emit-record`). A shard whose baseline is red fails on its own. Its job summary names each survivor and each
unmeasured mutant by line, with the verdict each pass gave it, and each shard's editor logs and results
files are uploaded as `Mutation EditMode shard N` or `Mutation PlayMode shard N`. The run
is the branch's own copy of the workflow and of `mutation_check.py`: a dispatch records its runs and
checks against the commit of the ref it names, so a campaign dispatched from the default branch would
be recorded against that branch's tip rather than the head it measured.

**A record run reports what it left to answer in code scanning.** `Mutation campaign ▸ report` runs after
every record run where a licence is configured, whatever the jobs before it concluded, and uploads a SARIF
file under the category `mutation-campaign` (`scripts/ci/mutation_report.py`). Each unanswered survivor
and each stale declaration the verdict printed is one alert at its file and line, under a rule named for
its operator, in **Security ▸ Code scanning**. An alert is keyed on the file and the mutation's own text
rather than its line, so a line shift keeps it, and an upload that no longer carries one closes it: a run
with nothing to answer uploads an empty list. A run with no verdict uploads nothing, since an empty list
would close every alert. Each mutant nothing measured, each mutant past the cap and each job that did not
pass goes to the run's job summary instead, and a mutant past the cap owes nothing: a later run measures
it. The job summaries of the mutation and kill-set workflows are cut to a byte bound by
`scripts/ci/step_summary.py`, since GitHub drops a step's whole summary past 1 MiB; the verdict job's shows
counts and the unanswered survivors by file, and the full list is in the uploaded verdict. Nothing decides on the alerts. A survivor is answered by a
pull request that writes the test or the declaration. A declaration a change writes brings the lines it
answers for into that change's mutants, so the next record run measures them: it answers the survivor,
or is stale where the mutant now dies. A test that
answers a survivor changes a test source, so every survivor's entry lapses and the next record run
measures them again, recording the one the test kills as killed; to see it before merging, measure its
line on the branch with a local run, which needs an editor:
`mutation_check.py --files <file> --lines <file>:<line>-<line>`.

**Every shard measures the plan's list, and a shard that did not report is not a survivor.**
`mutation-plan` writes the mutant list it counted (`--emit-mutants`) and each shard and the verdict job
hand it back (`--mutants`): a tree that generates another list refuses to run, rather than dealing its
indices to other mutants. A shard that died, was cancelled or timed its baseline out records nothing,
and the verdict job names it under `not measured`, with the shard's number: that is neither a
survivor nor a stale declaration, and a declaration over a mutant of such a shard is not reported
stale. An EditMode shard that died is named the same way, with the heading `EditMode`; its mutants are
in no PlayMode pass's list, and no PlayMode shard is named then, since a job runs its PlayMode pass only
after its EditMode pass passed. A PlayMode pass is named by the EditMode shard whose job ran it, and
`Mutation PlayMode shard N` is that job's. The run still fails. Re-running only the failed jobs of the run completes it: the shard
artifacts upload with `overwrite`, and the verdict job decides again over every record.

**A round is answered by a layer on top, not by an amend.** A finding cites the commit it was taken
on, so replacing that commit leaves the round and its answer inseparable, and the branch cannot land
without a force-push. `.claude/hooks/refuse/amend_of_published_commit.py` refuses `git commit --amend`
when a `refs/remotes/*` ref reaches HEAD, and when git could not say whether one does. Amending a
commit git placed and found unpushed is the ordinary case, and is what the predicate leaves alone.

**What the split costs.** A mutant the rewriter declines is one editor launch, or two where its narrowed
run did not kill it; one it places costs a domain reload and the test framework's preparation of a job
for each stage it runs in its shard's session, which [Generators~/README.md ▸ The Unity
assemblies](Packages/com.velvet.core/Generators~/README.md#the-unity-assemblies) describes. The figures
below were measured before sessions, over launches alone; how many mutants a diff generates is
measured in that section, against the window it names. A mutant's launch-compile-run measured
100–118 s on a developer machine against a 94 s baseline. `--plan` makes one EditMode shard for every five mutants, as many as `--max-shards`, and the deal
sets how many each holds, up to `SHARD_CEILING`; it stops adding shards there, because each shard pays for an image pull, a licence activation and a baseline
before its first mutant; the PlayMode pass of a shard's survivors pays its own activation and baseline in the same job, not an image pull, and each of its mutants costs more than twice an EditMode one measured on the same run.

**Shard count under an account's concurrent-job cap.** GitHub Free runs 20 hosted jobs at once across the whole account. One pull request's `Test` and `Source generators` workflows ask for 12 of them within a second of each other, measured on one pull request's runs of 2026-10-25 (head 5c2ddbb5): the licence check, repository settings, release notes, publication and the Python base-red job ended in 2, 3, 10, 15 and 17 s, and the other 8 held a slot for minutes (the Unity suites 9 and 13 min, the editor compilation 6, the generator suite 5, the three player compilations and the test-quality job 3 to 4); the aggregates start as those end and add nothing. A campaign's shards hold a slot each for 54 to 168 minutes. With `max-parallel` at 12 up to five of a pull request's jobs wait, none longer than the 17 s the longest short job takes, and the eight long ones fit beside the twelve shards exactly; at 13 the long ones and the shards would want 21 slots, and the last long job to ask would have no short job to wait for but only a shard or another long job. `ConcurrentJobBudgetTests` derives the 12 as 20 less the long jobs it counts from the two workflows, for `mutation-shard` and for the kill set's `shard` job alike, and `RecordBudgetTests` sizes `RECORD_SHARDS` as 12 shards in each of the four waves a day holds. It was 10, set when a pull request's `Test` was about five jobs. Nothing is left over: a second pull request's CI, or the run a push to `main` starts, queues behind shards, and the kill set dispatched beside a campaign asks for 24; none of that overlap has been timed. `MAX_SHARDS`, the default of a local `--plan`, stays at ten. A cap is a reason not to add a PlayMode job per survivor, which is why that pass runs in the shard's own job. Measured over 121 EditMode shards of the 100 pull-request runs
completed on 2026-09-26 and 2026-09-27, before a mutant was narrowed, a shard spent a median of 98 s
before activating, 82 s of it pulling the image, 36 s activating and 211 s on its baseline, and its
645 mutants took a median of 190 s. Over the 461 EditMode baselines those runs uploaded, the
tests took a median of 159 s, 47 s of it in the fixtures reading this tree's text, which is why no
launch runs them; `Reconciler/`'s assemblies took 5.9 s, `Styling/`'s 26.4 s and `Component/`'s
122 s, so the last runs its mutants on the whole suite. Over the 2284 EditMode mutants those runs
recorded, the area's own assemblies held a failing case for 83% of those in `Reconciler/` and 75% of
those in `Styling/`. On the pull request that moved
the campaign here, ten shards ran beside the three other licensed jobs, and all thirteen activated. Measured on the pull request that
added the PlayMode pass, over three mutants only a PlayMode fixture kills — one EditMode shard, which
reported all three surviving, then two PlayMode shards, which killed all three — the EditMode shard
took 13m05s with each mutant 144–149 s; the PlayMode shards took 20m35s and 14m32s, spending 97–130 s
before activation, 32–37 s activating, 358–374 s on a baseline of the same 209 cases `Test ▸
unity-tests` passed, and 340–364 s on each mutant; plan to verdict took 34m20s.

Over the shards of the campaigns dispatched from 09:06 to 19:56 UTC on 2026-10-01 — 200 EditMode and
67 PlayMode, and every one of them that measured a mutant ran a session — a shard spent at most 202 s
before its baseline, 375 s on an EditMode baseline and 625 s on a PlayMode one, and 145 s on an area's own
launch. A shard that finished spent at most 12 s after its last verdict, and from the end of its
baseline to its last verdict, less its areas' own launches, a median of 145 s a mutant on EditMode and 790 s on
PlayMode, where 84 of the 115 mutants those shards held took a launch of their own. The slowest
PlayMode shard that finished spent 934 s a mutant, over two. The slowest EditMode shard spent 493 s a
mutant over sixteen: the job's timeout cancelled it, so each launch it had not finished is taken at the
mean of those it had; its session's confirmation under the unmutated program failed, and 15 of its
16 mutants took launches of their own. `ShardCeilingTests` charges the slowest of the figures measured over 37 EditMode shards of two campaign runs on head 26bb6f99: fourteen of the forty shards of the first, which took 6 h 50 min from the licence check to the report and held six CodeGen mutants a shard, every one declined by the rewriter, so none made a reach run; and shards 0 to 22 of the second, read while it ran. A baseline took 362 to 716 s (charged at 716), the job 139 to 225 s from its start to the campaign's (225), and a launch of a mutant's own over the whole suite that ended by itself, 152 of them, 134 to 721 s, a median 0.95 and at most 1.06 of its shard's baseline (721); eight launches were stopped at their bound. A launch of an area's own assemblies, 170 to 291 s, was not measured again and is charged as before, in the arm that takes main's path (eight areas, though a shard touches two). 22 of the 23 shards of the second run made the reach run, the twenty-third placing three mutants. It took 0.95 to 1.04 of its shard's baseline, a median 0.99, and is charged at 1.04 where it finishes and at its bound of two baselines where it never does. The launch of the fixtures reading IL that follows a session survivor under `Runtime/` took 199 to 495 s over the 14 the logs show, 0.52 to 0.71 of a baseline (495). In 13 of the 22 sessions the shard's first launch of its own began 225 to 537 s after the session ended, an interval no log line accounts for and nothing here charges apart from the mutants'. The 88 mutants those 22 sessions placed cost, from the end of the reach run to the end of the shard's EditMode pass, less the launches of the declined mutants, a mean of 592 s each and a median over shards of 542, from 38 to 1346 s, or 1.03 baselines at the mean with 0.07 to 1.95 across shards: a shard whose four were all killed over their covered cases spent 153 s on them, and one whose session's work was repeated on launches of their own spent 5384 s, 1346 each. That slowest is within three seconds of what the reach arm charges a placed mutant, the whole suite and the launch of the fixtures reading IL (716 + 495) beside its gate at a tenth of the baseline (72) and the confirmation (60). The confirmation stays an allowance: the job logs show no line that separates it from the stages around it. In the second run the shards' EditMode pass took a median 4639 s and at most 8435 s (shard 11) against the budget of 10200 s, and the jobs 54 to 168 minutes; the first run's took at most 4736 s and 62 to 103 minutes. `SHARD_CEILING` is the most mutants for which all three arms fit the job's timeout, 5 on EditMode (the small arm at 7965 s of 10800, the reach arm at 9490 s, the arm whose reach run never finishes at 9819 s; six would charge them 8686, 10833 and 11090 s), and the test derives it from the timeout rather than reading it back. It was 6 when a reach run was charged at twice the baseline whether it finished or not and the launch of the fixtures reading IL at 291 s: the first fell by 647 s a shard and the second rose by 204 s a mutant. The budget above is what holds a shard that behaves worse than the charge.

**What the reach run saves, what a CodeGen mutant costs, and what a session loses.** In the four shards of the earlier run read for this, none made the reach run: each held six CodeGen mutants, which the rewriter declines, and a session that was assigned a declined mutant was not run. The reach run is now made by any EditMode shard that places `REACH_MIN_MUTANTS` mutants, whatever else it holds, and 22 shards of a later run made it (the section above has its measurements). It is the whole suite once with the switches recording and cost 0.99 of a baseline at the median. Against it a placed mutant saves the work of main's path: on the one shard of that run that took the path, three placed mutants took 781 to 806 s each (1.33 to 1.37 of its 587 s baseline), against a mean of 1.03 baselines for a mutant a session placed, a saving of about 0.3 of a baseline. Four mutants save 1.3 baselines and pay for a run of 0.99, three save 0.9 and do not, which is where the minimum of four stays; it rests on that one shard and on the spread of 0.07 to 1.95 baselines a mutant between sessions, so it is a reading and not a margin. A CodeGen mutant keeps a whole-suite launch of its own. The weaver rewrites the assemblies that reference Velvet when they are compiled, so a fixture anywhere in the suite can observe it; `WeaverReflectionProbeTests`, which calls the loaded `Unity.Velvet.CodeGen` assembly's weavers directly, and `CompilerILPostProcessorE2ETests`, which specifies the post-processor, both in `Velvet.Tests.Hooks.Editor`, are some of the cases that can, not all. Narrowing to them first would put a launch ahead of the whole suite for every mutant they do not kill, and of 24 CodeGen mutants in four shards of that run 22 survived the whole suite and two were counted as kills because their editor never finished (a literal `false` made `true` in `ReactiveScopeVolatility.cs`, on two shards), so no case killed one. Each survivor then also takes a PlayMode launch of 248 to 279 s in its shard's second pass, beside 354 to 400 s on EditMode on three shards whose baselines were 377 to 408 s and 656 to 666 s on the shard whose baseline was 693 s. A session's kills stood only if the confirmation closing their segment ran in the same launch (`confirmed_for`), so a launch that ended in a later mutant of the segment, as the first launch of each session on six shards of that run did, lost every kill before it; a confirmation now follows each mutant, and the verdicts of a launch are recorded as it ends and those of a mutant no case ran before the session starts, so a campaign stopped between launches keeps them. The EditMode session that makes no reach run stays off: it runs the stages a mutant's own launches run, and nothing measured shows it faster. Four mutants took 1345 to 1440 s as a session on three shards, and on one of them the two survivors under an assembly some fixture reads the IL of took 179 s and 174 s more on launches after it; on launches of their own a narrowed kill took 181 s and a survivor of a narrowed and a whole launch 498 and 524 s.

**Mutants are dealt by expected cost, not by position.** `--plan` and every `--shard` job compute the same assignment from the mutant list alone: `deal` takes the mutants longest first and gives each to the shard charged least so far, ties going to the lower shard and then the lower index, and prints each shard's mutant count and expected seconds with the spread between the cheapest and the dearest. A mutant under `Runtime/<Area>/` on EditMode is charged `NARROWED_COST`, 592 s, and any other mutant 721 s on EditMode, or 934 s on PlayMode, where nothing is narrowed and the deal comes to an even count. The 592 s is the mean cost of a mutant a reach run placed (the section above defines it over 88 mutants, with its spread of 38 to 1346 s); the 721 s is the slowest launch of a mutant's own over the whole suite that ended by itself, which is what the small arm of the ceiling charges, so no mutant is dealt dearer than that arm charges it. They are different statistics, so the ratio is rough. The plan has no baseline, so it cannot know which areas `narrowable` declines (those over a third of the baseline's test time) nor which narrowed mutants survive and take the whole suite as well, and a previous campaign's timings are not kept (the record holds verdicts, not seconds); a mutant dealt as narrowed may therefore cost more than its figure, which skews the balance. A shard is never given more than `SHARD_CEILING` mutants however cheap they are, so a shard of cheap mutants can hold as many as the ceiling, and makes the reach run like any shard that places `REACH_MIN_MUTANTS` or more, which `ShardCeilingTests` charges. A plan over `--max-shards` shards of the ceiling is refused.

`Test ▸ test-quality` holds the half that needs no editor: that the mutants can
be generated at all, and that every declaration in the package is one the script would accept rather
than one it silently refuses.

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

**A pull request that only deletes tests is checked by comparing kills, not by the campaign.** Mutants are
placed in package sources outside `Tests/` and `TestUtilities/`, so a pull request that touches nothing
else has none: a campaign over it reports no mutable change and passes whatever it removed. What such a pull
request claims is that every removed case was redundant, and that is a statement about two trees over the
same production source: each mutant the base tree's tests killed is killed by what the head leaves.
`.github/workflows/kill-set.yml` measures it. It is dispatched by hand, is required by nothing, and runs on
no event, because every mutant it measures costs the whole suite on two trees.

```bash
gh workflow run kill-set.yml --ref main \
  -f files="Packages/com.velvet.core/Runtime/Foo/Bar.cs Packages/com.velvet.core/Runtime/Foo/Baz.cs" \
  -f lines="Packages/com.velvet.core/Runtime/Foo/Bar.cs:120-260 Packages/com.velvet.core/Runtime/Foo/Baz.cs:40-180" \
  -f assemblies="Velvet.Tests.Foo.Editor" -f base=main -f head=my-pruning-branch -f platform=EditMode
```

`files` names the production sources whose mutants the removed cases could have been killing, each taken
whole unless `lines` restricts it. `head` must leave them, and `Assets/MutantSchemata`, byte-identical to the merge base, or the plan
refuses: the campaign's runner is tracked in the project and switches with the tree, so a pull request that
changes it is not a pruning. The plan also refuses a path that is not a mutable package source, so a
mistyped path fails the run rather than passing over nothing. `assemblies` is the test assemblies of the
removed cases and of whatever is meant to keep killing what they killed: a kill by an assembly left out
of it is not made on either tree, so a mutant the removed cases killed and only an omitted assembly would
still kill reads as lost, and leaving it empty measures the whole suite. `base` is the branch the pull
request merges into, resolved to its merge base with `head` by `scripts/ci/change_base.py`. The `scripts/`
that run are the head's own on both trees, so a head cut before `kill-set.yml` landed has no such
tooling. A run is one platform over one list of files: a pull request that needs more starts more runs.
`lines` is optional: space-separated `PATH:FIRST-LAST` ranges of the named files, which keep only the mutants
on those lines. The filter runs where the list is made, so the plan, every shard and both trees measure the
same filtered list, and the capacity check below is on that count. The plan prints what each range keeps,
what each file keeps of its own mutants, and the total. It fails for a range outside its file or reversed,
for a path `files` did not name or skipped, for a file named in `files` that has no range, and for any
single range that keeps no mutant. Choose the ranges from the members the deleted cases reach, and cover
every member a deleted case can reach: a mutant left outside the ranges is one the comparison never asks
about, and a removed case can have been the only one to kill it.
Listing the mutants needs no editor:

```bash
python3 scripts/test_quality/mutation_check.py --files <the same paths> --list
```

The plan refuses a list over the campaign's own ceiling, `SHARD_CEILING` mutants a shard in at most
`MAX_SHARDS` shards, because the sizing is the campaign's: each shard job measures one tree, as a
`mutation-shard` job measures its step, and `ShardCeilingTests` charges both alike. A mutant's launch is
stopped at its own run's baseline times `TIMEOUT_FACTOR` plus `TIMEOUT_CONSTANT` seconds (`mutant_bound`), and a launch that reaches the bound is a kill, `killed (timed out)`;
`--timeout` bounds the baseline alone. `ShardCeilingTests` charges one hanging mutant at the bound
of the baseline it charges.

Each workflow launch of the campaign's editor image (`mutation.yml`'s two measuring steps and
`kill-set.yml`'s) is given a memory limit, with swap at the same figure, and a process limit, so that a
mutant whose editor outgrows the limit loses a process inside the container. `mutation_check.py` records that
launch as `killed (the editor crashed)` ([`Generators~/README.md`](Packages/com.velvet.core/Generators~/README.md) owns the reading), and
`RunnerLimitTests` holds the limits on every such launch and the memory below a hosted runner's.

Each shard is measured on the head and on the base, in two jobs, by the default campaign: narrowed to an
area or to the cases that ran the site first, and on the whole suite wherever that did not kill. The
diff job, which runs only if every shard passed, runs `scripts/test_quality/kill_set_diff.py` over the two
sets of records. It matches mutants by file, line, column and rewrite and never by index, and compares
verdicts only. It fails, and names each one, when a mutant the base killed is not killed on the head — a
survivor, or a verdict that measured nothing — and a mutant that timed out on the base is a kill
the head has to match with a kill or a timeout. It cannot make the comparison, and says so, when a
mutant has a record on one tree only, when a base verdict is neither a kill nor a survivor nor a
non-mutant (an uncompilable mutant, for one), and when the base killed none and so proved nothing. A kill
the head makes that the base did not is reported and passes.

Say in the pull request body which `files` and `assemblies` were measured, the run's link, and the summary
line the diff job prints (`N mutant(s); killed on the base: …, lost on the head: 0, …`), beside the
deleted cases.

Pruning trips two other checks as well:

- **Adding or changing a line in a `[TestCase]` method poses it; deleting a row alone does not.** `base_red_check.py` poses a
  case when a line the branch added or changed lands inside the case's span, which includes the
  attribute lines above the method. A deleted row is a hunk with no added line (`changed_lines_by_file`
  reads it as zero lines), so a pruning that only removes rows poses nothing. Where the same pull
  request also adds or changes a line of the method, its body or an attribute line, and not only a comment, the method is posed to the base run, where
  the remaining rows pass, and it needs a refactor declaration above it. Deleting a whole case poses
  nothing either.
- **A deleted case's lines in `scripts/test_quality/neuter_holes.txt` and
  `scripts/test_quality/assume_gate_baseline.txt` have to go with it.** `Test ▸ test-quality` runs
  `neuter_check.py --audit`, which fails an entry for a case its fixture no longer declares, and
  `assume_gate_check.py`, which fails an entry the scan no longer finds. A case with no line in either
  needs nothing there.

A declaration for a method that is posed reads, above it:

```csharp
// GREEN_ON_BASE(refactor): the remaining rows are cases this method already ran.
```

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
tests. A withdrawal decides a case before this reading is reached, so a platform where withdrawals
covered every case would leave it unread; there it is read anyway, and a platform nothing vouched
for fails rather than passing on excuses.

Which cases are the branch's own is decided by comparing each case's code — its own text with the
comments, and a Python file's docstrings, blanked out — against the base's, since a diff over a large
rewrite describes untouched text as re-added, and a comment edited inside a case body is a changed line
there. So a change that edits only comments or docstrings poses nothing, wherever in the file they sit,
and the cases kept out are named on a line of their own — `out of scope: 12 case(s) of <file> hold a
line this branch changed and no code it changed` — because an empty plan is equally what a branch that
changed no test file at all leaves. Nothing the plan poses is named there: that line is what says a
case owes no GREEN_ON_BASE declaration, and the run gives every case the plan poses a verdict.
Alongside that, the cases the branch left alone in a file it changed nothing shared in are the base's
own text, so one of those going red means the tree is answering about itself and that fixture's
verdicts are withdrawn. Change a `[SetUp]`, a field, a private helper or anything under
`TestUtilities/`, and those cases stop being the base's text and stop being read as the instrument —
the run says which and why.

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

Three kinds of case belong on the base, and say so above themselves with a reason a reviewer can weigh:

```csharp
// GREEN_ON_BASE(characterization): the keyed-reorder order this refactor must not change.
```

```python
# GREEN_ON_BASE(refactor): the settle-path names this rename preserves.
```

```csharp
// GREEN_ON_BASE(construction): both sides here are the repository's own content. Misspell the
// reference as `published_check.unpublished_reasonn` and this is what reddens.
```

`characterization` pins behaviour the base already has; `refactor` rides with a change meant to preserve
it. `construction` is for neither: a guard that reads two of the repository's own sources against each
other is green on the base because both sides there are the base's content and they agree, so nothing
about the base's behaviour is pinned and the change is not behaviour-preserving. What shows such a case
can fail is a perturbation, which no base run can perform — so its reason has to name one, in backticks,
and the check refuses one that names nothing. The rule is on this category alone: asked of the other
two it would rewrite the corpus rather than check it, most of whose reasons name nothing in backticks.

A declaration answers for the change written under it, and it is read three ways so it cannot outlive
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
On pull requests, each `Test ▸ unity-tests` platform runs the C# base-red lane on the same runner
after its branch results have passed their provenance checks and been uploaded. It runs in at most
four rounds where the local
run takes up to `--max-rounds` of them: a base that cannot build one carried file writes no results
for anything, so the workflow withdraws what the static comparison above proves before its first
round, and what each round's own editor log blames before the next — every carried file the
compiler named, put back to the base's text at once, and the ones it names again at that text taken
out of the tree, since their cases already read as unbuildable and putting back what it failed
against would cost every file still building against the branch's text its reading. Both flows
withdraw by that rule, and the local loop alone keeps a second for a round whose log names nothing:
one silent file at a time, which it says as it goes rather than making it the case's verdict. A
file the log blames that the branch did not carry is the base failing to build itself, and neither
flow spends a further round on it. A last round that still writes nothing measured nothing, fails,
and prints the local command.

**What CI leaves to the author is two kinds of case.** A case reported `could not compile there` or
`could not load there` is accepted on the surface it names, and its assertion never ran without the
change — so a test calling a new API passes this check whether or not its assertion would have failed
on the old behaviour. A case whose declaration is a `construction` one is read as green as declared,
and of its perturbation the check asks only that the reason name one, which no base run performs. For exactly
those two kinds, show the failure yourself — run the case against a cut that breaks the fix while
keeping the surface it names, or against the perturbation its declaration names — and quote the
failure text where the change is reported. Every other case this check measures in CI, as
`Test ▸ unity-tests` measures it green, and a local run of those is optional.

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
exiting 0 unread looks exactly like one that checked.

Before those three it asks whether the run and the sources are of one tree at all. The log names the
project the editor opened; if that directory is on this machine and is not `--project`, the reading
is refused rather than taken. Pointing it at another worktree's results with `--project` left to
default measured exit 0 where the two trees declare the same fixtures, and where they do not it
names a fixture as a stranger — the sentence for a stale `Library`, about work that is fine. Where
the results file *sits* is not the question: a run whose project is the checkout may write its
results outside it.

Two runs are stood aside for rather than compared, because their tree is not here: one inside a
container, which names the container's path and is what CI reads, and one whose tree has since been
deleted — the base tree the base-red harness builds and removes. A results file of either read
against this checkout's sources passes unremarked, and what keeps the second out of the way is the
rule that a directory argument is not descended into. It still cannot separate two runs of the same
project writing a results file of the same name; what answers that is the headless recipe's
directory per run, not the guard.

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
about an entry is what [AGENTS.md ▸ Tests](AGENTS.md#tests) prescribes, and the check prints it. Not every
entry is a defect: the reading is a shape, so an environment precondition reached through a local the
Act declared is in the record too — the panel root's resolved width, read through the element the Act
mounted. Where inspection says an entry is that, answer the other new ones first — `--write-baseline`
rewrites the whole record from the tree and cannot take one entry — then regenerate it, check the lines
it added are the ones being answered for, and say so in the pull request.

**What it does not reach** is a gate above the `// Assert` marker over state the Act changes without
declaring it — a field, or a member of something the Arrange built. That is the shape the rule itself
was written from. Separating it from a legitimate precondition needs to know whether a regression can
falsify the assumption, which the text does not say, and the filters tried instead select so much of
the suite that the record would stop being a list anybody reads. A case carrying only that shape is
not in the record; what still reaches it is `assert_no_inconclusive.py`, on the day it fires.

### Checking that a Python case asserts one value

`scripts/test_quality/tuple_assert_check.py` reports where a Python suite under `scripts/` or
`.harness/` packs several values into one assertion, the shape [AGENTS.md ▸ Tests](AGENTS.md#tests)
rules out:

```bash
python3 scripts/test_quality/tuple_assert_check.py
python3 scripts/test_quality/tuple_assert_check.py --write-baseline scripts/test_quality/tuple_assert_baseline.txt
```

It reports an assertion whose first argument is a tuple or list display of two or more elements,
where the other compared argument is a display too or the first holds an element that is not a
literal. A name argument is followed to the display last assigned to it earlier in the same function,
so `got = (code, "x" in said)` then `assertEqual(got, (2, True))` is reported; a name bound any other
way is not followed. It reads the tracked suites named test_*.py under those roots where the project is a git
work tree, and every such file otherwise.
`scripts/test_quality/tuple_assert_baseline.txt` records the ones written before the rule, for
the reason `assume_gate_baseline.txt` does, and both a new entry and one the scan no longer finds fail
the check; a rewrite regenerates the record in the same change. It runs in `Test ▸ test-quality`. It
does not read C#, and it does not read a display written in the call second against a computed value,
which the text cannot tell from a computed value compared with an expected tuple.

### Checking that a removal did not strand its name

A comment naming a method is true until somebody deletes the method, and nothing was reading for
that: `DocumentationDriftTests` resolves the names markdown spells and strips comments from every
format that has them. Three claims about a slot-rebase mechanism shipped that way and were found by
grepping for the identifier months later.

```bash
python3 scripts/test_quality/stranded_name_check.py --base origin/main
```

It reads the change's own removals — a type or a method the diff takes out — and asks whether the
name still occurs on a code line in any C# source in the tree. If it occurs only in comments, it
says so.

The direction is what makes it decidable. Asked from the comment side it is not: over this package's
comments, bare PascalCase tokens resolving to no declaration numbered 518 for 4 real ones, and the
shapes with a clean ratio — a `cref` attribute at 0.46%, a name with a trailing call, a dotted pair
with both halves PascalCase — hold none of the four. Asked from the removal side it was two firings
over 320 commits.

What it declines to judge, and this list is what it claims rather than everything it misses:

- a name surviving only inside a string literal, which is content rather than a reference;
- a name surviving only in a `#region` or `#pragma` label, which is prose on a line that declares
  nothing — routed to the code stream instead, one such label answers for that name tree-wide;
- a property or a field, since the yield above was measured over types and methods and a widening is
  a fresh measurement rather than an edit;
- a declaration removed from C# written inside a verbatim string, which the diff reading cannot tell
  from one removed from the source around it;
- a comment that went stale for any reason other than a removal in the same change — a renamed
  parameter, a reversed condition, a mechanism that still exists and no longer does that.

There is no way to declare an exception, and one of the two firings did not want one: a
base-red declaration, of the refactor category, naming the base tree's method is a true sentence that does not
need the identifier to say what it says.

### Repository scripts

`scripts/` holds the harnesses, grouped by what they are for — `test_quality/` (what a test run
proves, and what the sources carry), `release/` (cutting a release and publishing it), `hooks/` (the
suites for the guards under `.claude/hooks/`, and the checks holding those guards to their
contracts), `generators/` (the committed generator DLLs), `pr/` (settling a pull request), `ci/`
(which commit a workflow job reads its change against) and `unity/` (the Library seed, sample sync,
and `asset_meta_check.py`, which `Test ▸ test-quality` runs to refuse an asset under `Assets` or
the package and its `.meta` committed apart). Two rules keep
the tree readable:

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

### Agent harness

Claude Code, Codex, and Cursor share these owning sources:

| Content | Edit here | Client entry points |
| --- | --- | --- |
| Repository instructions | `AGENTS.md` | `CLAUDE.md` imports it; Codex and Cursor read it directly |
| Hook registration and timeouts in seconds | `.harness/hooks.json` | `.claude/settings.json`, `.codex/hooks.json`, `.cursor/hooks.json` |
| Hook policy and shared helpers | `.harness/hooks/` | `.claude/hooks/` and `.codex/hooks/` are relative symlinks |
| Skills | `.agents/skills/` | Codex and Cursor discover it directly; `.claude/skills/` is a relative symlink |
| Agent prompts | `.harness/agents/` | `.claude/agents/` is a symlink; Codex/Cursor descriptors instruct the agent to read the owning file |

The instruction entry point follows [Claude's documented AGENTS.md import](https://code.claude.com/docs/en/memory#agents-md).
The skill root is a documented discovery location for both
[Codex](https://developers.openai.com/codex/skills) and [Cursor](https://cursor.com/docs/skills),
so Cursor needs no extra skill link. For ecosystem practice,
[Vercel's skills installer](https://github.com/vercel-labs/skills#installation-methods) recommends
symlinking to a canonical copy, and [agentcanon](https://github.com/buildermethods/agentcanon#the-convention)
uses `.agents/skills/` as that copy. These are interoperability choices, not a requirement
that hooks and agent-role schemas share one format.

[Cursor's Claude compatibility](https://cursor.com/docs/reference/third-party-hooks) can reuse
Claude hook configuration, but requires an enabled account feature and the third-party configuration
setting. This repository keeps native Cursor hooks and role descriptors so discovery does not
require that setting. Hook registrations use seconds, as specified by the
[Claude hook reference](https://code.claude.com/docs/en/hooks#common-fields).

Hook code, skill content, and agent prompt bodies are not copied. Editing the owning file
is visible through the links or reference without synchronization. Commit symlinks as symlinks;
a checkout that materializes them as plain text cannot discover those skills or execute the hooks.

Run `python3 scripts/harness/sync.py` when changing hook registration or agent discovery metadata.
Only the client-specific JSON/TOML and thin Markdown descriptors are generated. `--check`
checks those adapters and the links without writing; CI runs it. The command refuses to
replace a real directory with a link, so it cannot discard a pre-existing local harness.
Personal settings are outside this generator.

`scripts/harness/adapter.py` translates Codex and Cursor events before invoking the shared
policy. Cursor's `Shell` becomes `Bash`; shell working directories and relative file paths
are resolved before the guards run. Codex's `apply_patch` becomes a `Write` for each affected
path. The adapter reconstructs exact-context add/update/delete/move patches without modifying
files. Missing or ambiguous context is refused before editing; retry with more exact context.
The shell guards retain their existing command-parser limits.

Repository-wide backlog and pull-request state do not establish the scope or completion
criteria of the current task. The clients therefore register neither `open_backlog.py` nor
`unsettled_pr.py` on Stop. Assigned issues do not authorize starting another task, and passing
PR checks do not authorize merging. Their scripts live in `scripts/pr/diagnostics/` for explicit diagnostics;
completion is judged against the user's request. A future blocking Stop policy must identify
work accepted by the current task and its agreed completion condition.

Hook session identifiers are passed to the deferral reader, and non-Claude refusal messages
contain the literal session identifier needed to write a deferral. No cross-session identifier
is inferred when the event lacks one. Cursor stop refusals become follow-up messages and are
subject to Cursor's loop limit; aborted/error stops do not resume. Cursor subagent reports go
to the Hooks log because its subagent-stop output offers continuation rather than an
observational context field. Agent tool restrictions remain client-specific: Claude's
`disallowedTools` is not translated to an OS sandbox; the shared reviewer prompt retains
its read-only instructions.

Python 3, Git, and the existing guards' GitHub CLI dependencies must be available to the
client process. Enable/trust project hooks in each client and restart an existing session
after changing agent or skill definitions. Client loading and runtime behavior follow the
[Codex hook contract](https://developers.openai.com/codex/hooks) and
[Cursor hook contract](https://cursor.com/docs/hooks); adapter tests exercise payloads and
guard subprocesses, not the applications' hook loaders. Avoid enabling Cursor's additional
Claude-hook import alongside its native generated hooks, which would register the guards twice.

The linked `.claude/hooks/` paths below remain available to existing guard tests.
The owning `.harness/hooks/` directory is grouped by what a script is able to stop:

- `refuse/` — `PreToolUse`. Stops the tool call, by exiting 2 with the reason on stderr or by
  answering with a `permissionDecision` of `deny`.
- `report/` — `SessionStart`, `SubagentStop`, `PostToolUse`. Stops nothing: `PostToolUse` fires
  after the tool has already run, so it writes into the transcript and exits 0 whatever it finds.
- `lib/` — imported by the rest, wired to no event of its own.

Its suites under `scripts/hooks/` are run by one sweep over the directory, so adding a guard adds no
workflow line. `WorkflowTriggerCoverageTests` refuses a step that names one, and the sweep's own
comment in `generators.yml` says why.

A Python-only edit here can redden a C# fixture, because several read the directory rather
than any one guard. Which ones is derived rather than listed, since a list of thirty files
goes stale the first time somebody adds a seventeenth:

```bash
grep -rl 'claude/hooks' --include='*.cs' --include='*.py' Packages scripts
```

A guard over state that is shared rather than owned by one session — a branch, the stash — belongs
in `.harness/hooks.json`, which generates the project registrations. An agent's frontmatter can only narrow
that: a guard named there and nowhere else is absent from the main session and from every other
agent type. Such a guard says so with a `HOOK_SCOPE = "session"` line.

A `PreToolUse` guard is reached only for the tools its `"matcher"` names, and acts only on the tools
its own gate admits. It declares the second as a `HOOK_TOOLS = {...}` set and gates on that set
rather than on a literal, which is what leaves the two halves comparable. Register it under the tool
names themselves: a matcher naming no set a check can read is refused — `"*"`, an empty string, and a
`PreToolUse` entry carrying no `"matcher"` key at all, which is the shape the `SessionStart`, `Stop`
and `SubagentStop` entries take. A guard covering several tools spells them out in the matcher.

Two of them cover `Bash` as well as the editing tools, because a session making its changes with
`sed`, a heredoc or a short script is outside an editing-tool matcher by construction and meets no
refusal at all. Read back over this project's own session transcripts, those writes include edits to
`.claude/settings.json` and to the guards' own sources — the registration that decides what any of
them is routed. `.claude/hooks/lib/tracked_writes.py` is the reading the two share: the literal
operand of a redirect, of an in-place `sed`, and the destination of a `cp` or an `mv`, each put to
`git ls-files`. That is narrower than deciding what an arbitrary command writes, which is the
problem the body guard above was reduced for twice — and narrow from the same transcripts rather
than from a guess about what a session does. `tee` reached a tracked file in none of them, and
`git mv` wants a criterion of its own rather than another spelling of that one. What the reading
gives up it does not leave a reader to infer: `tracked_writes.UNREAD` is the list, the refusal is
built from it, `scripts/hooks/test_tracked_writes.py` holds the refusal to every entry and counts
them, and every entry has a case posing the shape it names.

Reading a redirect on a heredoc opener's line, and anything after a `<<<` here-string, meant
changing `mask_shell_literals`, which every caller of `command_segments` shares. What decided it was
measured on those transcripts rather than judged: over them the change makes `log`, `push`,
`status`, `merge` and `diff` newly visible and none is in the set the guards read, while it corrects
`git commit`'s operand list in all 55 readings of it that move — the masker before it appended the
following command's tokens there, and the two verdicts that change both go from refusing to
allowing, one of them a commit refused over an unexpanded operand belonging to a command three
further along the line. A
`git add` or a `git stash` written after an opener does become visible, and is refused correctly;
what the corpus shows is that none occurs, not that none could.

`edit_while_a_ready_pr_sits.py` reaches the verdict it reaches for an edit, for the shell writes it
can place on a tracked file — an Edit of an untracked file is held and a redirect onto one is not.
`changelog_into_closed_version.py` cannot: its reading compares the section before against the
section proposed, and a shell command carries no proposed text, so it refuses a write onto a
CHANGELOG whatever that write would have said and names the tools that present the text.

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

A third way is being right about the wrong branch. Two of the preconditions over a merge were once
asked of a base by naming `main` — whether the head contained it, and whether it held an unpublished
release — for as long as `main` was the only branch taking pull requests. So the day a maintenance
line was cut, both refused its release outright and no case disagreed: nothing in the repository
named a second branch at all. The base is the pull request's own field, read through
`.claude/hooks/lib/merge_target.py`, and `scripts/hooks/pull_request_base_check.py` poses every
guard in the directory a pull request based on a branch that is not `main`, in a repository where
`main` holds a failed push run and an unpublished release and the named base holds neither. A guard
that judges either of them against `main` fails it without anybody having remembered to write a
case.

A fourth is being right about the wrong tree. A `PreToolUse` hook is handed the directory the tool
call started in, and `cd <worktree> && gh pr create` runs somewhere else — so a guard reading that
directory answers about a checkout the command has left, and prints a positive verdict about a tree
it never opened. Two false refusals of that shape were reported before this, one guard both times,
and what nobody asked afterwards is which of the others read a directory at all. The sweep below is
what counts them, so no count is kept here. `command_directory` in
`.claude/hooks/lib/shell_commands.py` owns the reading, and what a guard handed its decline may not
do is answer about the directory it started in. It matches one shape — `cd` to a literal path, one
or several, joined by `&&`, `;` or a newline, all of them ahead of the work — and a step outside it
ends the match; the module states what the reading then does with the rest, and why matching one
shape beat walking the operators. Reading a leading `cd` was already shared before that; placing its
answer was written three times at three call sites and the three disagreed, one of them against the
hook process's own directory rather than the event's.

The decline is only reached where the guard already has something to judge. These run on `Bash`, so
they see every command in the session, and one that reads the directory before establishing its
subject refuses `cd - && ls` — naming a command the user never typed, with the move as the whole of
its reason. Measured: four guards each refused seven such commands before the order was settled.

`scripts/hooks/test_cwd_resolution_check.py` poses each guard registered on `Bash` six runs: four
carry a move — one it could place, that same move carrying the redirection that silences it, one
nothing can place, and that unplaceable move carrying a command the guard has no subject in — and
the other two are the bare command in each of the two trees, which is what scores a guard that
addressed no tree at all. A guard declaring some other tool poses no Bash command and is skipped;
one declaring `Bash` with no probe command to pose is a fault, because a guard the sweep cannot pose
is a guard it holds to nothing. It reads two things off each run: the verdict, from the exit code
and from a `deny` decision on stdout, and which tree the guard's own `git` and `gh` calls addressed,
from shims that record where they were run. The tree reading is what separates a guard whose subject is not the tree
from one that read the wrong tree and had nothing to say about it either way, and where neither
reading speaks the check reports undecided rather than agreement — a guard that lands there wants a
case in its own suite, as `library_seed_without_room.py` has, since it asks the filesystem rather
than git and the shims see nothing of that. What the check does not decide is which way a guard
should go once it has declined to place a move: refusing and standing down are both defensible and
the guards differ, `tracked_writes.py` naming its own gap and `edit_while_a_ready_pr_sits.py`
standing down. That suite's stand-in guards ride alongside, between them producing every outcome the
sweep reports — a case compares the two sets rather than a sentence claiming it — so a sweep whose
shims have stopped recording fails rather than coming back clean. Two trees that stop being two are
the degeneracy the decided count cannot see, and the sweep compares them to each other for it. A
guard that raises is the third: it exits 1, which is neither the code that allows nor the code that
refuses, and read as an allow it scores exactly as a guard whose subject is not the tree — measured,
five of nineteen guards replaced by files that raise on import and the sweep came back clean. So an
exit that is neither is reported as the fault it is.

The explicit PR diagnostics declare the same policy and are held to one thing more, because blocking was never
what they got wrong. They blocked, and described the pull requests rather than the reading — so the
deferral the message invited named the API error instead of whatever the work was waiting on.
`.claude/hooks/lib/repository.py` owns both halves of the remedy: a second way of asking, drawn on a
different quota, before blindness is declared at all, and the sentence a block has to carry when it
is. The same check runs every guard in `scripts/pr/diagnostics` and requires that sentence of one that
blocks. It poses two modes: nothing answers, and — the one a second way of asking creates — the
listing answers while every per-pull-request read fails, which is where a guard can report on a
pull request it learned nothing about. An empty answer is posed as neither, being the ordinary state
`open_backlog.py` acts on. A guard whose own question is answered in a mode that broke somebody
else's reading declares `UNREADABLE_ALLOWS`, with a comment and with a sibling that refuses there,
so an exemption no other guard stands behind is reported rather than taken.

A fifth way is a guard nothing poses at all. The checks above each ask one dimension — an
unreadable reading, a wiring, a base, a tree — and `merge_unproven_head.py` agreed with every one of
them on `main` while exiting 1 on a `gh` that failed after answering, and reading a check list `gh`
reported under a non-zero exit as no list at all. `scripts/hooks/test_hook_coverage.py` is the floor
under that: every script under `refuse/` and `report/` has to be named in the **code** of a
tracked test source, comments and docstrings taken out first, so that a summary naming a guard does
not count as a case posing one. A guard whose subject is decided in a module of its own names that module's suite in a
`DECISION_TESTS` tuple instead — `merge_onto_unpublished_release.py` does, the release state it
refuses on being `scripts/release/published_check.py`'s answer — and the floor fails a name it
cannot resolve to a source it reads. Named is weaker than posed, deliberately: what a name match
says is that some case exists, not that the case asks anything.

`scripts/hooks/test_hook_verdicts.py` is the posed half: it runs each of those scripts against an
event it decides against and a near miss it lets through, and its docstring owns how.

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
| `Test ▸ player-compile` | push (filtered) / every PR / merge group | **required** (skipped if absent) | no |
| `Test ▸ newest-editor-compile` | push (filtered) / every PR / merge group | **required** (skipped if absent) | no |
| `Test ▸ release-notes` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ publication` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ test-quality` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ base-red-python` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ Required checks (Unity)` | push (filtered) / every PR / merge group | not required | **yes** |
| `Benchmarks ▸ benchmarks` (PlayMode) | push (filtered) to `main` / every PR labelled `benchmarks` / manual | **required** (skipped if absent) | no |
| `Benchmarks ▸ trend-main` | push (filtered) to `main`, after `benchmarks` | not required | no |
| `Benchmarks ▸ trend-pull-request` | every PR labelled `benchmarks`, after `benchmarks` | not required | no |
| `UPM ▸ split` | push to `main` / manual (`workflow_dispatch`, which also tags and publishes the release) | not required | no |
| `Docs` (DocFX → GitHub Pages; also carries the benchmark chart, as of its last deployment) | push (filtered) / release / manual | **required** (skipped if absent) | no |

The two required checks are aggregates, and the real jobs are not required themselves. A required check
that does not run stays `Pending` and blocks the pull request with nothing able to clear it, which is what
a trigger filter, a matrix change or a rename would each cause. The aggregates carry no trigger filter, `needs:`
the real jobs, and pass when every dependency is `success` **or** `skipped` — the second is what lets a
fork with no `UNITY_LICENSE` merge, since `unity-tests` is skipped in exactly that case. Any other
result fails them, with one exception: a pull request or dispatch run whose results besides those
are all `cancelled`, and whose pull request (or dispatched branch) now points at another commit,
asks for its own cancellation and its aggregate ends cancelled rather than red, which still blocks a
merge of that head. Where that cancellation is refused or does not land, it fails. On `push` and in
the merge group a cancelled dependency fails them whatever the branch holds now.
`scripts/ci/aggregate_results.py` owns the decision, for both aggregates.

The mutation campaign has no row: it gates no merge, since no branch-protection rule requires a check
of it, and [Checking that the tests can fail](#checking-that-the-tests-can-fail) owns when each of its
jobs runs and where it reports.

In `test.yml` and `generators.yml`, filtering therefore applies to `push` only, by branch as much as by path — so a pull
request runs both workflows whether it is based on `main` or on a maintenance branch, and so does every
merge-group entry once a queue is turned on. The `merge_group:` keys are there for that, and
`WorkflowTriggerCoverageTests` fails if either of the two gated triggers goes missing from either
workflow, or gains a child key whose colon follows its name under one of them. Skipping work per queue entry is a job-level condition, not a
trigger filter: a required check that does not start has nothing able to clear it.

Every job of both required workflows carries a `timeout-minutes`, so one that hangs gives its runner
back rather than holding it while other pull requests' jobs queue. `test.yml` says how each is sized,
and `scripts/ci/test_job_timeouts.py` fails for a job that has none.

A job that reads a change against a base takes that base from `scripts/ci/change_base.py`, whose
docstring says which commit it names for each event; `scripts/ci/test_change_base.py` fails when a step
passes `--base` anything else.

`main` does not require heads to be up to date before merging. That setting serialises the queue — each
merge invalidates every other branch's run — without testing the
combination it exists to protect any better than a merge does. What does test that combination is a merge
queue, which is a separate change. `settle.py merge` and the `gh pr merge` guards do not require it
either: a pull request whose own checks passed on its head merges while behind, once GitHub reports no
conflict.

What they require instead is a green base: they refuse a merge onto a base whose required workflows
last failed on push, so a red `main` is fixed or reverted first. A head is exempt where it contains the
failing commit and its own Unity suite jobs concluded success, which is how the fix and the revert land;
`Required checks (Unity)` passes a suite job skipped for want of a licence secret, so a skip is not a
run. A first attempt still in progress is passed over, so it refuses nothing and the newest verdict
among the last 20 push runs stands; a re-run in progress reads as a failure until it finishes. A base
with no push runs has no verdict and refuses nothing, which is every maintenance branch: both workflows
run on push to `main` alone. `scripts/pr/red_base.py` decides all of that, and `settle.py` and
`merge_unchecked_against_base.py` both ask it.

They also require the head to contain its base's newest release commit — the commit that dated a
CHANGELOG section, which `published_check.release_commit` finds. A branch cut before it filed its entry
under `[Unreleased]`, a clean merge can file that entry inside the section the release closed, and
`breaking_in_flight_check.py`, which reads the dated sections, runs on pull requests rather than on
push. So a head behind a release takes its base in with `settle.py update`, and its pull request checks
run again over the result. A head behind only commits that date nothing is not refused.

And they refuse a head that has to outlive its merge — `main`, `upm` or a maintenance line — because
`settle.py merge` squashes the head and deletes it. `scripts/pr/long_lived.py` decides which heads
those are, and `settle.py` and `merge_unchecked_against_base.py` both ask it; the maintenance-line
section says how a line lands.

The source-generator tests and the `upm`-branch split run with no Unity license, so the pipeline
works out of the box on a free account. The Unity EditMode/PlayMode job is skipped automatically
unless a license secret is configured.

### Panel fixture timing

Every fixture built on `PanelTestBase` creates, shows and closes an editor window per case. Each such
case records how long that took, apart from its body, through `PanelPhaseTimings` in `TestUtilities`:
the window's creation and `Show` (`create_show`), its `Close` and `DestroyImmediate` (`teardown`), each
forced layout/styles pass made while the case is open (`layout`) and the whole span from the base
`SetUp` to the end of the base `TearDown` (`case`). A `layout` row falls inside the body or the setup,
so it is not added to the others, and `teardown` leaves out the unmount before it, which only `case`
holds. A layout pass in a derived `TearDown` before it calls the base one falls inside the `case` span;
one after it is not recorded. A further window a fixture opens itself is not measured, and a case
ignored for want of a graphics device records nothing.

A run whose editor was given `-testResults` writes the rows to `panel-phase-timings.csv` in the
directory of the file that argument names, one flush per fixture, with no flag to pass; a run without it
writes nothing, and no directory is created for the file. A failure to record or to write is swallowed,
not reported as a failed case. The EditMode job of `Test ▸ unity-tests` uploads the directory as
`Test results (EditMode)`, so the CSV is in that artifact, and runs `scripts/ci/panel_phase_timings.py`
over it. The script prints the count, total and mean per phase, the window cost as a share of the panel
cases and of the suite, and the fixtures with the largest window cost, to the log, the job summary and
a `::notice` annotation on the job's check run, which the REST API serves where the summary is not.
A run that wrote no CSV is a `::warning::` and a summary line, not a failure, and a run with no results
XML reports the suite share as unknown. The suite share takes the panel cases from the `case` rows,
which hold their setup and teardown, and the rest from the results XML, so that part follows whatever the
runner's per-case duration covers for cases that are not panel cases.

### Benchmarks

The PlayMode fixtures in the `Velvet.Tests.Performance` namespace measure with Unity Performance
Testing and assert nothing about their numbers, so they run in two modes. `BenchmarkRun` in
`TestUtilities` picks the mode: by default a benchmark takes one measured iteration and no warmup, and the editor flag `-velvetBenchmarkFull` restores the counts
each one declares. `Test ▸ unity-tests`, the base-red reading and the mutation campaign pass no flag, so
the required PlayMode run carries only the one-iteration form. A new benchmark takes its counts from
`BenchmarkRun.Warmup` and `BenchmarkRun.Measurements` rather than from literals. A path that only a later
iteration reaches (an unmount that follows a mount, a mount that rents what an earlier unmount pooled)
goes unexercised in the one-iteration form, so a benchmark whose body has one runs it once more
unconditionally after its loops. Each fixture's body was read for such a path when this was written;
a new benchmark is held to the same reading. A local run that wants real numbers passes the flag
itself, and the flag is necessary for them rather than sufficient.

`.github/workflows/benchmarks.yml` runs the full counts, as its own workflow and not a required one:

- on every push to `main` that changes the package (documentation and generator sources excluded), and
  on a manual dispatch;
- on a pull request carrying the **`benchmarks`** label, at the label being added and at every push
  after. The label has to exist in the repository; nothing in the workflow creates it. A newer push
  cancels the run still measuring an older head, and a run for any other event (another label being
  added, say) is in a group of its own, so it cancels nothing.

A run uploads the results XML as `Benchmark results`, writes the medians as a table to the job summary
and uploads them as `Benchmark medians`, in the JSON shape `benchmark-action/github-action-benchmark`
reads (`scripts/ci/benchmark_results.py` converts, `scripts/ci/test_benchmark_results.py` holds it). A
benchmark that throws fails the run like any other test. The history is not gated on a number:

- `trend-main` appends a push's run to the `benchmark-data` branch under `dev/bench`, one entry per commit
  and at most 200 in the chart. It first creates the branch as an orphan commit holding a README when
  the remote has none, which the action does not do itself, and it is the only job holding
  `contents: write`. The `Docs` workflow copies that directory into the
  site it deploys, so the trend chart is at <https://s4k10503.github.io/velvet/dev/bench/>; it
  refreshes with the next docs deployment, so it can trail the newest entry by a merge, and the site
  lacks the page until `main` has recorded a first entry and a docs deployment has followed.
- `trend-pull-request` compares a labelled head with the latest entry on `main` in the job summary and
  writes nothing.

The numbers are one consistent CI series, not absolute ones: the editor runs them with
`-debugCodeOptimization` and the code-coverage recorder on, because game-ci passes both whatever the
workflow asks for (`test.yml` says so beside its coverage options), so the history is for comparing runs
with each other.

Both flag a median above 150% of the previous entry in the summary and neither fails: CI runners'
timings are noisy enough that a gate would be a coin flip. A run of this workflow is a run for the head
like any other, so `scripts/pr/expected_checks.py` waits for an unfinished one on a labelled pull
request, and a failed newest run blocks its merge.

### Merging a pull request

A pull request labelled `automerge` is merged by `.github/workflows/automerge.yml` once every check on
its head has passed or been skipped; every other pull request is merged by hand after review, with
`settle.py merge`. The label means *review has settled: merge when every check on the head has passed
or been skipped*: add it once review has settled, and remove it to hold the pull request. Labelling
takes triage access or above, so only an account holding that can opt a pull request in. The workflow
does not create the label; it has to exist in the repository.

No merge waits on a mutation campaign. A campaign dispatched onto a pull request's
branch runs on its head like any other workflow, and `settle.py merge`, the automerge runs through it, and
`refuse/merge_unproven_head.py` leave its runs and its checks out of what they read, so one that failed
or is still running holds nothing. They leave out the `Mutation campaign` commit status as well,
which a publisher wrote on pull request heads while the campaign gated their merge, and which a head
can still carry. `scripts/pr/campaign.py` says which runs, checks and status are a campaign's. [Checking that the tests can fail](#checking-that-the-tests-can-fail) owns where a survivor
is reported instead.

Adding the label hands the pull request off, and so does marking a labelled draft ready for review:
neither starts a run whose completion would ask, so `scripts/pr/automerge.py` dispatches the merge run
at once, and it waits for the hand-off's own run to finish, whose job is a check on the head. A push or
a reopen starts the head's `Test` run, which asks when it passes. A hand-off from a pull request event
logs a failed reading or dispatch as a warning and still passes: its job is a check on the head, and a
failed one would have settle and the hook refuse that head, a merge by hand without the label included.

The merge goes through `settle.py merge`, run from a checkout of the default branch and never of the
pull request, so every precondition above applies, and a refusal is logged with the run still
succeeding. It asks again about one pull request when `Test` or `Source generators` passes a run for
it, when a hand-off dispatches it, and when dispatched with its number (*Actions ▸ Automerge ▸ Run
workflow*). It asks about the open pull requests carrying the label when `Test` or `Source generators`
passes a push run on `main`, which is what clears a red base, and when a release dispatch of `UPM`
passes, which is what clears an unpublished release. A refusal cleared by anything else — a `Test` run
dispatched by hand onto the head, for one — waits for the next of those events or for a manual
dispatch.

A completed run whose head is no longer the pull request's is left to the newer head's runs, and a head
on a fork is refused, as `settle.py` refuses one by hand. A hand-off reaches a pull request based on a
maintenance line only where that line holds the workflow: the pull request events run the workflow
file the base branch holds. A completed run's merge job reaches the pull request either way.

The merge is made with the `AUTOMERGE_TOKEN` secret rather than the workflow's own token, because a
merge made with `GITHUB_TOKEN` starts no workflow: `main` would get no push run for it, and the
green-base precondition above reads the required workflows' push runs, so a break an automerged change
carried would go unseen until some later push. The `upm` split would wait for that push too. Without
the secret, standalone merge and sweep jobs report an error and fail before reading or merging.
It is a fine-grained personal access token
for this repository alone, with Contents, Pull requests and Workflows read and write, and Actions,
Checks and Commit statuses read. The merge is attributed to the token's account, and `protect-main`
holds it as it holds anyone: it lists no bypass actor. The hand-off dispatches the merge run with the
workflow's own token instead, granted Actions write.

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

A CHANGELOG entry opens with `- ` and sits outside a fenced block. Four readers in
`scripts/release/` each decide where an entry ends and they disagree about the alternatives: a `*`
bullet reads as no entry at all, so a breaking section written with stars reads as empty and the
drain question a major is held to passes over the breaks it exists to catch.
`test_release_notes.py` refuses both spellings.

The CHANGELOG holds two open sections, and which one an entry goes in is settled when the entry is
written rather than when the version closes. `## [Unreleased]` is what a minor or a patch may ship.
`## [Unreleased — breaking]` is what has to wait for a major: an API a caller has to edit around, and
behaviour a working application would notice changing.

A change does not write either section in the file. It adds one fragment, a markdown file under
`Packages/com.velvet.core/Changelog~/unreleased/` for `## [Unreleased]` or under
`Packages/com.velvet.core/Changelog~/breaking/` for `## [Unreleased — breaking]`, named for its
branch with each `/` written as `-` (so a branch named fix/list-keys writes fix-list-keys.md), since a fragment
sits directly in its directory: pull requests appending to one spot in `CHANGELOG.md` conflict with
each other whenever one of them merges, and two that write different paths do not. A change onto a
maintenance line whose tree has no `scripts/release/compile_changelog.py` writes its entry into that
line's `CHANGELOG.md` instead, as the maintenance-line section below gives. A fragment is one or more `### <kind>`
headings, each opening with its entries. The kinds are `release_notes.FRAGMENT_KINDS`; Highlights is
not one of them, since it is written when the version closes. `release_notes.compose` refuses a
fragment with any other heading, with text above its first heading, or with a subsection that does
not open with a `- ` entry, and any file under `Packages/com.velvet.core/Changelog~/` outside those
two directories that is not a dotfile; `test_release_notes.py` composes this repository's fragments.
`published_check.py`, `breaking_in_flight_check.py` and `test_release_notes.py` read the open
sections through it, and it files each fragment's entries under its kind after the entries the file
already holds there — so an entry written into the file itself before fragments existed stays valid,
and is carried into a version exactly as the fragments beside it are. The `upm` split strips the
directory.

1. Close the version in `Packages/com.velvet.core/CHANGELOG.md` and bump `version` in
   `package.json` to match. Run `scripts/release/compile_changelog.py` first: it writes that
   composed reading into the file and deletes the fragments, so the sections hold every entry
   before anything else is done to them, and `published_check.py` refuses a change
   that closes a version while `## [Unreleased]` still lists an entry, which is what renaming it
   before compiling leaves. `compile_changelog.py` reads the fragments HEAD holds and refuses one on
   disk that HEAD does not hold as it stands, and the note builder, given `--dispatch` as `upm.yml`
   gives it, refuses a commit that still holds a fragment — one merged after the release pull request went green would otherwise ship
   in this version and be described in the next. A major moves the breaking entries up into `## [Unreleased]` and leaves
   their heading standing with none. A minor or a patch closes only over a section already empty,
   and `published_check.py` refuses one that is not: a release publishes the tree rather than the
   section, and `main` was found carrying the code an entry described while that entry still
   waited. A line that never took those changes has an empty section, which is what keeps the
   reading silent on the maintenance line. Rename
   `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` **last**, once nothing further is going into it:
   `changelog_into_closed_version.py` refuses a write that adds to a dated section, or that changes
   one whose version the remote tags — `vX.Y.Z-main`, which the UPM dispatch leaves on the commit
   each release was dispatched from, whichever line that was — otherwise than the dated-section
   rule below allows; and the rename is what dates it. The breaking heading itself is never
   dated and never deleted, and a `**Breaking:**` bullet in `### Highlights` belongs to a major and
   to no other release; `test_release_notes.py` refuses each of those. Where the entries went is
   read from the change rather than the file, by `published_check.py` on the pull request that
   closes the version: a major has to close with the section empty and every entry of it word for
   word in the version being closed, and a minor or a patch may neither take anything out of it nor
   leave anything in. So a wording change belongs in a change that closes no version.
   `breaking_in_flight_check.py` reads an entry by its whole first line, and a continuation line is
   not read at all, so a first line reworded — kept as the start of a longer one included — reads as
   one lost, whatever the change closes, unless a fragment under
   `Packages/com.velvet.core/Changelog~/breaking/` records the correction: the line
   `<!-- corrects: - <the old first line, as written> -->` under the entry that rewrites that line.
   That change and the next one closing a version then read the old line as carried by the new one,
   and `release_notes.compose` drops the correction line from the composed CHANGELOG, which is what
   `scripts/release/compile_changelog.py` writes and the note is built from. An entry written in the
   file's own section is corrected by moving it into such a fragment in the same change. A change
   closing no version may also move an entry out of the section, and is asked nothing about it then;
   what the move decided is asked of the next change closing a version, which
   `breaking_in_flight_check.py` reads against every commit of `main` from the newest `vX.Y.Z-main`
   tag the result descends from to the change's base: an entry the section held at any of them, that
   the result carries neither there nor in a major closed since the tag, is refused. So an entry
   that has sat in the section on `main` since the last release leaves it only into a major — moved
   out by one change and closed into a minor by the next, it is refused at the second — and deciding
   it was never breaking is open to a change that also closes a major
   carrying it, or to one made before the entry reaches `main`, since the reading cannot tell that
   decision from a break slipping into a minor. The pull request's own commits are not read, so a
   line written and corrected on the branch costs nothing. Every dated section whose version the
   remote tags is held besides, whatever the change closes and whichever line published it, since
   the note is the tag's and the file cannot tell a correction from a deletion: it has to be the
   base's but for a line its own tag's copy has that the base is short of, put back where that copy
   has it, heading and date included, and leaving no `###` heading of the section over nothing
   that the base did not already leave so — so a heading goes back only above something it comes to
   head, an entry put back in the same change or lines the section already carries, and not where
   it empties the heading above it — the base rather than the tag's copy, because `main`'s older
   sections were reworded and reordered after their releases and carry a Highlights block their
   tags' copies do not, so the copy says which lines may go back and the base says what is there —
   one the file has not got, a maintenance line's carried forward, arrives as that copy and
   nothing else, that copy being the only text of it here and an addition to a note already
   published belonging in the release that follows it, and one gone from the file that the base or
   the last release on this line carried is refused too. A dated section whose version no tag
   names is unpublished and not held. Where the remote tags no release the result descends from —
   a repository before its first release — the breaking section is read one step deep and the
   check's pass says so; a remote that cannot be listed is refused as unread instead, and so is a
   history cut short under the result, where a tag the result is not found to descend from may sit
   below the cut — a cut elsewhere, a maintenance line fetched shallow, is not. A tag whose commit
   the checkout has not got is passed over, as a maintenance line's is, until the result carries a
   dated section for its version; reading that release's note is then what fails, and that is
   refused as unread too. A major also answers for the breaking work still in flight: an entry
   written for it sits on its own branch until that branch merges, so the CHANGELOG holds only the
   part that has landed. Name every open pull request adding to
   `## [Unreleased — breaking]` and say which the version carries —
   `breaking_in_flight_check.py` refuses one that names none of them, and "not this one" is a
   decision it accepts. Give the version a row in `SECURITY.md`'s
   supported-versions table, and decide there what happens to the series it succeeds:
   `supported_versions_check.py` refuses a release the table does not cover with one row marked
   supported.
2. Merge to `main` (the `upm` branch is updated automatically).
3. Run the **UPM** workflow via *Actions ▸ UPM ▸ Run workflow*, entering the same version.
   This tags `vX.Y.Z` on the `upm` (package-at-root) commit and publishes a GitHub release.

**A release is gated on the mutation record and on `main`'s CI.** The dispatch promotes the commit it
runs on, which CI has already measured, the way
[React's release scripts](https://github.com/facebook/react/tree/main/scripts/release) promote a commit
of `main` that CI built rather than building a release of their own. Before anything is tagged,
`upm.yml` runs `scripts/release/mutation_gate.py` over that commit, which refuses it unless:

- where the default branch holds the commit, whichever ref the release was dispatched from, the newest
  completed push run of each required workflow (`red_base.REQUIRED_WORKFLOWS`) at the commit passed;
- the record answers every mutant of the campaign's scope at the commit
  ([Checking that the tests can fail](#checking-that-the-tests-can-fail) owns the scope and the
  record): each holds an entry that still stands, and every survivor among them is answered by a
  declaration. A mutant past a run's cap, never measured, or measured to nothing is unresolved, and
  refuses the release.

The gate checks the commit out into a worktree of its own and reads that, so nothing uncommitted in
the dispatch's checkout reaches it, and runs that commit's copy of the campaign's scripts, the copy its
record runs measured with; it refuses a commit whose tree has no record tooling. It refuses a shallow
repository too, where the scope's tag and cutover can be cut off the history and the scope comes out
empty: `upm.yml` runs it before the step that fetches `main` at depth one. Its mutation half reads
git and the record alone, so the same commit and record give it the same answer. Where it refuses,
its log names each mutant: wait for the record runs to catch up, dispatch one
(`gh workflow run mutation.yml --ref <branch>`), or answer the survivors by a pull request.

**The way past it** is the dispatch's `skip_release_gate` input: a reason written there releases
without either check, and the run's log carries it as a warning. It is for a record that cannot be had —
the branch lost, the runners down for days — and not for a survivor nobody answered.

Between step 2 and step 3, `main` names a version that does not exist. The dispatch builds the note
from the CHANGELOG section, which was written before anything merged in that window and describes none
of it — so those commits ship inside the release, undescribed. v2.0.1 spent a day there and took twelve
merges, and publishing it meant tagging the release commit by hand and dispatching from the tag, since
dispatching from the branch would have shipped all twelve.

So the window is guarded: `settle.py merge` and `gh pr merge` refuse a pull request based on the
branch whose window is open, and `Test ▸ publication` fails for one whose checks run in it.
`scripts/release/published_check.py` decides it and states the repair in its own message.

**A pull request that went green *before* the release landed keeps that result.** This repository sets
`strict_required_status_checks_policy: false` so the Unity matrix is not re-run for every base
move, so the merge button on github.com stays enabled for it. What refuses there is
`merge_onto_unpublished_release.py` and `settle.py`'s publication precondition, neither of which
github.com consults; turning the strict policy on is what would close it server-side, at the cost
that buys.

**A green pull request left sitting starts refusing every edit.**
`.claude/hooks/refuse/edit_while_a_ready_pr_sits.py` refuses every editing tool once one has been ready
for fifteen minutes, and a Bash command whose literal operand names a tracked file with it, and the
instruction it prints is `settle.py merge`. Ready is that command's own
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

**Merging forward takes a merge commit, and the line outlives it.** A squash records no ancestry,
and `settle.py merge` squashes and then deletes the head, so it refuses a pull request whose head is
the line, as the `gh pr merge` guards do. Land it from the web interface with *Create a merge commit*,
and if GitHub's delete-on-merge setting removes the line afterwards, restore it with the pull
request's *Restore branch* button. The continuous-integration section says which other heads are
refused the same way.

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
attract it. A change onto a line cut before fragments existed, as `2.x` was, writes its entry under
that line's `## [Unreleased]` rather than as a fragment, and a fragment a pick carries comes out with
it: nothing in that line's tree reads one, so its note would leave the entry out and its package
would ship the file. `unreleased_maintenance_line.py` reports a fragment on such a line as one to
fix, `main`'s copy of `changelog_into_closed_version.py` sends a refused edit in such a tree inline
where that copy is the one running. Both only warn: the line's dispatch runs the line's own
`upm.yml` and scripts, so nothing refuses to publish a fragment there. `changelog_into_closed_version.py`,
which step 1 above relies on, is registered against `Bash|Edit|Write` and reads a shell command only
for a literal operand naming the file, so a cherry-pick does not reach it — and the line's own tree
does not carry that hook, so a session rooted there runs none.

**The record is the `-x` trailer.** Squash as everywhere else, and put the `(cherry picked from commit
…)` lines **in the pull request body**. Do not reach for
`git cherry`: measured here, it reported every commit it walked as absent from the line, including the
ones whose fix the line carries.

**A line's release is gated by the line's own `upm.yml`.** A dispatch runs the workflow file at the
ref it names, so a line cut after the gate existed carries the step, and its record is filled by
dispatching a record run onto it before the release, `gh workflow run mutation.yml --ref <line>`, which
dispatches the next until nothing is pending. Its scope starts at the later of the cutover and the
line's own newest release tag. A line cut before the gate existed, as `2.x` was, runs no gate and
carries neither the step nor the record tooling until both are picked onto it.

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
