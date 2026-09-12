#!/usr/bin/env python3
"""Print the -testFilter value for the fixtures the given test files declare.

A filter built from test file names can miss fixtures: one file may declare several, or none named
like it. This reads the files with base_red_check.py's reader instead, and follows each class a case
is written in to the concrete classes deriving from it, directly or through other concrete classes,
wherever they are declared.

Each fixture is one `^...$` term, and the terms are joined by `;` alone. How the runner matches a
value is the unity-tests skill's to say: the anchors keep a term from selecting a fixture whose
name begins or ends with it, and each separator is `[.+]` because the reader writes a nested type
after a dot where the skill notes the runner writes `+`.

    F=$(python3 scripts/test_quality/fixture_filter.py Packages/.../Tests/Editor/FooTests.cs) &&
      "$UNITY" -runTests -batchmode -projectPath "$PWD" -testPlatform EditMode -testFilter "$F" ...

Exits 1 with nothing on stdout where the files declare no fixture for the run, so the `&&` starts
none. Exits 2 where it cannot answer: a file is missing or outside the project, git cannot list the
project, the fixtures run on both platforms and no --platform chose one, a case runs under no
concrete class the reader finds, a requested file declares a caseless abstract base, or a fixture
is generic or takes or inherits fixture arguments, whose runner names this does not spell. Shared bases
without cases require an explicit fixture roster; they must not silently shrink a combined filter.
"""

import argparse
import importlib.util
import re
import subprocess
import sys
from pathlib import Path


def _sibling(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    path = Path(__file__).resolve().with_name(name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


_base_red_check = _sibling("base_red_check")

NOTHING_DECLARED = 1
CANNOT_ANSWER = 2

# A fixture argument written by position, or a fixture source. A named property is passed over,
# which leaves TypeArgs on a generic fixture to the check for generic declarations. Read over the
# raw text, because the code mask blanks a string argument to spaces and a blank reads as no
# argument at all; a comment that matches costs a refusal.
FIXTURE_ARGUMENTS = re.compile(
    r"\bTestFixtureSource\b|\bTestFixture(?:Attribute)?\s*\((?!\s*\))(?!\s*[A-Za-z_]\w*\s*=(?!=))")


def term(fixture):
    return "^" + "[.+]".join(fixture.split(".")) + "$"


def generic_declaration(name):
    return re.compile(_base_red_check.CSHARP_DECLARES + re.escape(name) + r"\s*<")


class Tree:
    """The project's C# sources and the inheritance read off them, taken once per run."""

    def __init__(self, corpus):
        self.corpus = corpus
        self.heirs_by_file = {relative: _base_red_check.concrete_heirs({relative: text})
                              for relative, text in corpus.items()}
        self.heirs = {}
        self.declared_by = {}
        self.inheriting = {}
        self.parents = {}
        self.caseless_bases = {}
        for relative, text in corpus.items():
            owners = None
            for line in _base_red_check.code_lines(text):
                declared = _base_red_check.CSHARP_TYPE.search(line)
                if not declared:
                    continue
                name = declared.group(1)
                bases = _base_red_check.CSHARP_BASES.search(line)
                if bases:
                    self.parents.setdefault(name, set()).update(
                        _base_red_check.CSHARP_IDENTIFIER.findall(bases.group(1)))
                if _base_red_check.CSHARP_ABSTRACT.search(line):
                    if owners is None:
                        owners = {case.fixture.rsplit(".", 1)[-1]
                                  for case in _base_red_check.csharp_cases(text, relative)}
                    if name not in owners:
                        self.caseless_bases.setdefault(relative, set()).add(name)
        for relative, heirs in self.heirs_by_file.items():
            for base, names in heirs.items():
                self.heirs.setdefault(base, set()).update(names)
                for name in names:
                    self.declared_by.setdefault(name, set()).add(relative)

    def declaring(self, name):
        """The files whose text declares a type of this simple name, comments included."""
        declares = re.compile(_base_red_check.CSHARP_DECLARES + re.escape(name) + r"\b")
        return {relative for relative, text in self.corpus.items() if declares.search(text)}

    def ancestry(self, name):
        found, queue = set(), [name]
        while queue:
            current = queue.pop()
            if current in found:
                continue
            found.add(current)
            queue.extend(self.parents.get(current, ()))
        return found

    def inherited_from(self, base):
        if base not in self.inheriting:
            self.inheriting[base] = any(
                case.fixture.rsplit(".", 1)[-1] == owner
                for owner in self.ancestry(base)
                for relative in self.declaring(owner)
                for case in _base_red_check.csharp_cases(self.corpus[relative], relative))
        return self.inheriting[base]

    def descendants(self, fixture):
        """The concrete classes deriving from `fixture`, directly or through other concrete ones."""
        found, queue = set(), [fixture]
        while queue:
            for heir in self.heirs.get(queue.pop().rsplit(".", 1)[-1], ()):
                if heir not in found:
                    found.add(heir)
                    queue.append(heir)
        return found

    def fixtures_of(self, relative):
        """(fixture -> the platforms it runs under, the files `unspellable` reads for it, the
        abstract classes here whose cases run under no class this reading finds)."""
        cases = _base_red_check.cases_in(relative, self.corpus[relative])
        orphaned = sorted({case.abstract_owner for case in cases
                           if case.abstract_owner and not self.heirs.get(case.abstract_owner)})
        here = {case.fixture for case in cases if not case.abstract_owner}
        named = {case.fixture
                 for case in _base_red_check.as_the_runner_names_them(cases, self.heirs)}
        involved = {relative}
        for base, names in self.heirs_by_file[relative].items():
            involved |= self.declaring(base)
            # A class here with no case of its own, deriving from a base declared elsewhere, is
            # named by nothing above.
            if self.inherited_from(base):
                named |= names
                here |= names
        found = {}
        for fixture in named | {heir for name in named for heir in self.descendants(name)}:
            where = {relative} if fixture in here else self.declared_by[fixture]
            involved |= where
            found[fixture] = {_base_red_check.platform_of(file) for file in where}
        return found, involved, orphaned

    def unspellable(self, fixtures, involved):
        """Refuse names requiring fixture arguments or generic fixture spelling.

        Ancestor declarations outside Tests/ carry attributes too. The source reader follows
        simple names, so ambiguous declarations and generic argument names can cost a refusal.
        """
        involved = set(involved)
        for fixture in fixtures:
            for owner in self.ancestry(fixture.rsplit(".", 1)[-1]):
                involved.update(self.declaring(owner))
        for relative in sorted(involved):
            text = self.corpus[relative]
            if FIXTURE_ARGUMENTS.search(text):
                return relative, "passes fixture arguments"
            for fixture in sorted(fixtures):
                for name in fixture.split("."):
                    if generic_declaration(name).search(text):
                        return relative, "declares {} generic".format(name)
        return None


def resolve(argument, project):
    return (project / argument).resolve().relative_to(project).as_posix()


def main(argv):
    parser = argparse.ArgumentParser(
        description="Print the -testFilter value for the fixtures the given test files declare.")
    parser.add_argument("files", nargs="+", help="the files, absolute or relative to --project")
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--platform", choices=("EditMode", "PlayMode"),
                        help="which run the value is for, where the fixtures span both")
    arguments = parser.parse_args(argv)
    project = Path(arguments.project).resolve()

    given = []
    for argument in arguments.files:
        try:
            relative = resolve(argument, project)
        except ValueError:
            print("{} is outside {}".format(argument, project), file=sys.stderr)
            return CANNOT_ANSWER
        if not (project / relative).is_file():
            print("no such file: {}".format(argument), file=sys.stderr)
            return CANNOT_ANSWER
        given.append(relative)

    try:
        names = _base_red_check.git(project, "ls-files").stdout.splitlines()
        corpus = {relative: (project / relative).read_text(encoding="utf-8", errors="replace")
                  for relative in names
                  if relative.endswith(".cs") and (project / relative).is_file()}
    except subprocess.CalledProcessError as failed:
        print("cannot list {}'s files: {}".format(project, failed.stderr.strip()), file=sys.stderr)
        return CANNOT_ANSWER
    for relative in given:
        if relative.endswith(".cs"):
            corpus[relative] = (project / relative).read_text(encoding="utf-8", errors="replace")
    tree = Tree(corpus)

    by_platform = {}
    empty = []
    for relative in given:
        if tree.caseless_bases.get(relative):
            print("{} declares abstract bases without cases ({}); name their concrete fixtures "
                  "from a results file's fullname roster instead".format(
                      relative, ", ".join(sorted(tree.caseless_bases[relative]))), file=sys.stderr)
            return CANNOT_ANSWER
        if _base_red_check.kind_of(relative) != "csharp":
            empty.append(relative)
            continue
        found, involved, orphaned = tree.fixtures_of(relative)
        if orphaned:
            print("{} declares cases in {} and no concrete class this reads derives from it; name "
                  "the fixtures they run under by hand".format(relative, ", ".join(orphaned)),
                  file=sys.stderr)
            return CANNOT_ANSWER
        if not found:
            empty.append(relative)
            continue
        refused = tree.unspellable(found, involved)
        if refused:
            print("{} {}, which can give a fixture a runner name this does not spell; name it "
                  "from a results file's fullname roster instead".format(*refused), file=sys.stderr)
            return CANNOT_ANSWER
        for fixture, platforms in found.items():
            for platform in platforms:
                by_platform.setdefault(platform, set()).add(fixture)

    if empty:
        print("{} of {} file(s) declare no fixture: {}".format(
            len(empty), len(given), ", ".join(empty)), file=sys.stderr)
    chosen = arguments.platform
    if chosen is None and len(by_platform) > 1:
        print("these fixtures run under {}; pass --platform for each run".format(
            " and ".join("{} ({})".format(platform, len(by_platform[platform]))
                         for platform in sorted(by_platform))), file=sys.stderr)
        return CANNOT_ANSWER
    if chosen is None and by_platform:
        chosen = next(iter(by_platform))
    if not by_platform.get(chosen):
        print("no fixture to run{}".format(" under " + chosen if chosen else ""), file=sys.stderr)
        return NOTHING_DECLARED
    for platform in sorted(set(by_platform) - {chosen}):
        print("{} fixture(s) run under {} and need a run of their own".format(
            len(by_platform[platform]), platform), file=sys.stderr)
    print(";".join(sorted(term(fixture) for fixture in by_platform[chosen])))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
