"""What a head is still owed beyond the checks listed on it.

A check list holds the checks that exist. A head whose `Test` run was still pending carried only
`Source generators`' checks, every one passing, and `settle.py` called it mergeable; the ruleset then
refused the merge because `Required checks (Unity)` had never reported. Two readings close that, and
each covers a case the other cannot:

- a workflow run for the head that has not completed, which catches the jobs no ruleset requires;
- a context the base's rulesets require that no check on the head carries, which catches a workflow
  whose run does not exist yet, so that no listing of runs can show it unfinished.

The first reading waits only on runs that exist, so a workflow a path filter stops is not a wall.
The second asks only for what the ruleset names: `generators.yml`'s repository-settings job holds
that to the two required-checks aggregates, and
`WorkflowTriggerCoverageTests` holds their workflows to subscribing to `pull_request` unfiltered.
`settle.py` and `refuse/merge_unproven_head.py` both decide from here.
"""

RUN_PAGE = 100

# The rules listing carries no total, so a page this full is read as one that may have dropped a rule.
RULE_PAGE = 100


def runs_path(slug, sha):
    """The listing, below the API root, of every workflow run whose head is `sha`."""
    return f"repos/{slug}/actions/runs?head_sha={sha}&per_page={RUN_PAGE}"


def rules_path(slug, base):
    """The listing, below the API root, of every rule the rulesets apply to `base`."""
    return f"repos/{slug}/rules/branches/{base}?per_page={RULE_PAGE}"


def unfinished(runs):
    """`<workflow> (<status>)` for each run in an Actions runs listing that has not completed.

    Every status but `completed` counts, so one GitHub adds later holds the merge until somebody
    classifies it — the rule settle.py's `_BUCKET` follows.
    """
    return sorted(f"{run.get('name') or run.get('path') or run.get('id')} ({run.get('status')})"
                  for run in runs if run.get("status") != "completed")


def required(rules):
    """The status-check contexts a rules listing requires, or None where the page may be partial."""
    if len(rules) >= RULE_PAGE:
        return None
    return sorted({check["context"]
                   for rule in rules if rule.get("type") == "required_status_checks"
                   for check in (rule.get("parameters") or {}).get("required_status_checks") or []
                   if check.get("context")})


def absent(contexts, names):
    """The contexts in `contexts` that no check name in `names` carries."""
    present = set(names)
    return [context for context in contexts if context not in present]
