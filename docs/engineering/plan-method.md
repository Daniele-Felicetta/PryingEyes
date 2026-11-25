# Running the plan: roles, cycle and order of merges

[modular-tasks.md](modular-tasks.md#how-to-carry-out-a-task) says how one agent carries out one task. This page is the other side: how
tasks are chosen, briefed, checked and merged when several run at once. It was written after the first wave (T01, T02, T04 and T10 ran
side by side) and keeps what that wave showed, including what went wrong. The plan, its order and the state of each task are in
[modular-roadmap.md](modular-roadmap.md).

## Roles

| Role | Does | Never |
| --- | --- | --- |
| Maintainer | Takes the [decisions that are theirs](modular-roadmap.md#decisions-that-are-the-maintainers); may hand the lead the starting and the merging of tasks | |
| Lead | Chooses the next tasks, writes their briefs, checks every report, runs the order of merges, keeps the roadmap and the progress numbers true | Carries out a task itself; takes a reserved decision; rewrites an acceptance criterion; uses or closes the maintainer's game |
| Task session | One task, or a chain of tasks that share files, in its own worktree and branch | Merges; rewrites a criterion that cannot be met; starts the next task before it is told |

The lead is a role, not a program: a session or the maintainer plays it. What it needs is in the repository, not in a conversation: the
roadmap table, the open pull requests and the census (`python tools/code_census.py`).

## The cycle

1. **Choose.** A task is ready when the tasks it depends on are `done` (merged, not only open) and its group in
   [what can run at the same time](modular-roadmap.md#what-can-run-at-the-same-time) has no other open pull request. Start only as many
   sessions as the machine and the maintainer can bear. A starting point, not a measured limit: two that need the editor at once, because
   each worktree holds a `Library` of about 6 GB and the machine is also the maintainer's.
2. **Brief.** The session starts from the task's section, so the brief adds only what the section cannot know ([below](#a-brief)).
3. **Carry out.** The protocol of [modular-tasks.md](modular-tasks.md#how-to-carry-out-a-task). The session stops at a draft pull
   request with a green gate and reports.
4. **Check.** A report is a claim ([below](#checking-a-report)).
5. **Merge,** one pull request at a time ([below](#the-order-of-merges)).
6. **Measure.** After a merge that moves a number, the table of [modularization.md](modularization.md#how-progress-is-read) is
   updated from a fresh census, and the briefs of the tasks that remain are read again against the code.
7. **Escalate** when [a rule below](#when-to-stop-and-ask) says so, with a recommendation, in one question.

## A brief

A brief fits in a screen:

- the task (id and title), and where to start: `origin/preview` at a named commit and a new branch with the task's id, never the branch
  of an earlier task (it was merged by squash);
- what has drifted since the section was written: numbers that moved (take them from the census), files that moved, what the open pull
  requests touch;
- the three things that go wrong: a criterion that cannot be met is reported and not rewritten; merging is not the session's; no check
  uses the maintainer's game;
- where to stop: the pull request is open as a draft and its gate is green on the head that is reported;
- what to report: the pull request and its head commit, the gate, each acceptance criterion with the number that proves it, and every point
  where the section did not match the code.

## Checking a report

Every check below is cheap and none needs the session:

| Check | How |
| --- | --- |
| The head is the one reported; the pull request is a draft and mergeable | `gh pr view N --json headRefOid,isDraft,mergeStateStatus` |
| The gate that passed is the gate of that head, not of an earlier commit | `gh pr checks N`, `gh run list --workflow gate.yml` |
| Each acceptance criterion is still the one in the brief | the diff of `modular-tasks.md` in the pull request: a changed criterion without a decision of the maintainer is a defect |
| The code that decides pass or fail does what the report says | read that part of the diff (the guard, the fixture, the workflow), not the whole of it |
| The pull request does not collide with the others that are open | `git merge-tree --write-tree <head of A> <head of B>` for each pair: it exits 1 and names the files |
| The numbers the report quotes | take again the cheap ones (`python tools/check_suite.py`, the census); a number that needs a build or a long run stays the session's, and is said to be so |

## The order of merges

Pull requests that run side by side share pages. In the first wave `testing.md` was edited by T02, T04 and T10, and
`modularization.md` by T02 and T04. Each was clean against `preview` when opened and conflicted after the first merge, so:

1. Merge one pull request by squash, when its gate is green on its head, and delete its remote branch (rule 12). The order frees the most
   work: first the task that others wait for, then the one with the most pages in common with the rest (it settles them first), then the
   smaller ones.
2. Watch the gate on `preview` for the merge commit (rule 8).
3. Each of the other open pull requests is updated by its own session: merge `origin/preview` into its branch (no rebase, no forced push),
   keep both sides in the pages, run `python tools/check_suite.py` and the unit tests, push, wait for the gate. Only then is it the next
   one's turn; updating all of them at once makes each conflict again after the next merge.
4. After the last merge of a wave: bring the maintainer's checkout up to date if it is clean, build and publish the game only if game
   code changed (rule 11), and take the census again.
5. A task that waits for another (T03 after T02, T05 after T04) starts when the one before it is merged, from the new `preview`.

## When to stop and ask

The roadmap lists the cases ([risks, and what stops the plan](modular-roadmap.md#risks-and-what-stops-the-plan)). In practice:

- **An acceptance criterion cannot be met as written.** The session measures, says what it measured and stops. T04 asked for
  `check_suite.py` in under five seconds; it already took 5.2 s before the change, because the wiki check, added after the brief,
  costs about 4.5 s by itself, and the census the guard is built on added 2.7 s. The maintainer chose to rewrite the criterion (the guard
  adds at most about three seconds, measured before and after), in a commit of its own, and the row stayed `done`. The other ways out
  were a task of its own for the slow part, and `blocked`.
- **A decision that is reserved** ([the list](modular-roadmap.md#decisions-that-are-the-maintainers)). The lead prepares the facts and a
  recommendation; the tasks that need the decision wait.
- **The gate on `preview` is red**, or a suite that passed fails for a reason that is not the task's.
- **A step would delete something that is not derived,** or would use the maintainer's game, registry settings or screen while they play.

## Work in the editor

A task that needs the editor runs in a worktree of its own with an imported `Library` (see
[agent-tooling.md](agent-tooling.md#toolsworktreeps1-a-place-to-work) and rules 9 and 12 of `AGENTS.md`), and no other Unity opens it.
Before anything that uses the graphics card or the screen, the session checks that the maintainer is not running a game
(`Get-Process`, `nvidia-smi`) and waits if they are: a test that competes with a game gives wrong numbers and spoils the game.

## What the first wave showed

- **A criterion written before measuring can be false.** T04's five seconds were. Sessions measure before they promise and put the
  numbers in the pull request.
- **A word in a brief is a decision.** "A lab" in T04, as written, would have flagged 14 files that are not violations: nine projects of
  `tools/multiplayer-check` (retired later by T20) compiled engine code against Unity's own libraries on purpose. The session found it by running the rule on the
  real tree, before writing a test, and defined a lab as a project that reaches no Unity assembly.
- **A check reads what its environment has.** The sparse checkout of the gate lacked the `.asmdef` files that the census reads, so the same
  census gave other numbers there. It showed on a sparse copy made with the gate's own patterns.
- **Shared pages are the price of parallel work.** The order of merges above exists for them; prefer tasks whose pages are their own (T05
  edits none of the shared ones).
- **Numbers drift while a plan waits.** The track mode added a lab and 20 engine-free files after the briefs were written. Take the census
  again before briefing.
