---
name: skill-jules-integrator
description: Sequentially pulls remote Jules sessions, verifies build compilation without running tests, manages git history, reverts conflicts/errors, and re-submits failed tasks against updated integration branches.
---

# Skill: Jules Integrator (skill-jules-integrator)

## Trigger Conditions
Activates after remote Jules sessions complete, when changes need to be sequentially integrated into the local active branch and pushed to remote origin.

## Integration Workflow

### 1. Sequential Pull & Integration
For each completed session in `.jules/active_batch.json`:
1. Ensure the integration working branch is checked out.
2. Pull the session diff into the working tree:
   ```bash
   jules remote pull --session <ID> --apply
   ```

### 2. Compilation Verification (No Test Execution)
- Compile/build the solution locally to verify compilation (e.g., `dotnet build Realm.slnx`).
- **Constraint**: Do NOT run tests during integration phase to maximize pipeline throughput.

### 3. Successful Integration Path
If cherry-pick apply and compilation succeed without errors:
1. Commit the changes using a standardized commit message format referencing a concise summary of the task description:
   ```text
   feat/refactor: [Task Summary Title]

   Integrates Jules session <ID>.
   Summary: [Brief task description summary]
   ```
2. Push the local integration branch to origin:
   ```bash
   git push origin <active_integration_branch>
   ```
3. Delete the remote Jules session to clean up remote workspace:
   ```bash
   jules remote delete --session <ID>
   ```
4. Record task in summary as `MERGED`.

### 4. Failure & Conflict Handling Path (Instant Revert)
If merge conflicts occur during pull or if compiler errors occur during build:
1. **Immediate State Revert**: Instantly restore working tree to a clean state:
   ```bash
   git reset --hard
   git clean -fd
   ```
2. Record task metadata (session ID, original prompt content, active branch name, error details) in a tracking list of failed tasks.
3. Record task in summary as `FAILED_QUEUED_FOR_RESUBMIT`.

### 5. Automated Re-submission Protocol
After processing all sessions in the current batch:
1. For each failed task:
   a. Delete the old failed session from Jules:
      ```bash
      jules remote delete --session <FAILED_SESSION_ID>
      ```
   b. Re-submit the task using the active integration branch name (which now includes newly pushed changes from successfully merged tasks):
      ```bash
      jules remote new --repo . --session "<prompt>" --branch <active_integration_branch>
      ```
   c. Record new session ID in `.jules/active_batch.json`.

### 6. Integration Summary Report
At the conclusion of the integration run, emit a summary report formatted as follows:

```markdown
## Jules Integration Summary Report
- **Total Processed Tasks**: [Count]
- **Successfully Merged & Pushed**: [Count]
- **Failed & Re-submitted**: [Count]

### Merged Sessions
- Session `<ID>`: [Task Summary Title]

### Re-submitted Sessions
- Session `<OLD_ID>` -> New Session `<NEW_ID>`: [Task Summary Title] (Reason: [Merge Conflict / Compilation Error])
```
