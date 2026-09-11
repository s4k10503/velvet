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
concrete class the reader finds, or a fixture is generic or takes fixture arguments, whose runner
names this does not spell.
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
    """The project's C# test files and the inheritance read off them, taken once per run."""

    def __init__(self, corpus):
        self.corpus = corpus
        self.heirs_by_file = {relative: _base_red_check.concrete_heirs({relative: text})
                              for relative, text in corpus.items()}
        self.heirs = {}
        self.declared_by = {}
        self.inheriting = {}
        for relative, heirs in self.heirs_by_file.items():
            for base, names in heirs.items():
                self.heirs.setdefault(base, set()).update(names)
                for name in names:
                    self.declared_by.setdefault(name, set()).add(relative)

    def declaring(self, name):
        """The files whose text declares a type of this simple name, comments included."""
        declares = re.compile(_base_red_check.CSHARP_DECLARES + re.escape(name) + r"\b")
        return {relative for relative, text in self.corpus.items() if declares.search(text)}

    def inherited_from(self, base):
        """Whether any case is written in `base`."""
        if base not in self.inheriting:
            self.inheriting[base] = any(
                case.fixture.rsplit(".", 1)[-1] == base
                for relative in self.declaring(base)
                for case in _base_red_check.cases_in(relative, self.corpus[relative]))
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
        """(the first file among `involved` passing fixture arguments or declaring one of these
        fixtures generic, which of the two it does), or None."""
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
        corpus = _base_red_check.corpus_of(project)
    except subprocess.CalledProcessError as failed:
        print("cannot list {}'s files: {}".format(project, failed.stderr.strip()), file=sys.stderr)
        return CANNOT_ANSWER
    for relative in given:
        if _base_red_check.kind_of(relative) == "csharp":
            corpus[relative] = (project / relative).read_text(encoding="utf-8", errors="replace")
    tree = Tree(corpus)

    by_platform = {}
    empty = []
    for relative in given:
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
