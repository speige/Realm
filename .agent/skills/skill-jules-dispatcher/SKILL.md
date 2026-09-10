---
name: skill-jules-dispatcher
description: Manages prompt batching, target branch routing, daily quota circuit-breaking, and Jules CLI task dispatching.
---

# Skill: Jules Dispatcher (skill-jules-dispatcher)

## Trigger Conditions
Activates when structured user story prompts need to be queued, batched, or dispatched to remote Jules agent sessions via the CLI.

## Jules CLI Environment & Executable Resolution
- **Command Name**: `jules`
- **Native Binary (Windows)**: `C:\Users\devin\AppData\Roaming\npm\bin\jules.exe`
  - **CRITICAL**: Do NOT invoke `jules.cmd` with multiline prompt arguments. Windows `cmd.exe` batch argument parsing `%*` truncates strings at the first newline (`\n`), discarding the actual prompt body. Always invoke the native binary `C:\Users\devin\AppData\Roaming\npm\bin\jules.exe` directly and pipe the prompt via `stdin`.
- **CLI Commands Reference**:
  - `cat prompt.txt | jules remote new --repo <repo_path_or_slug>`: Creates a new remote session using stdin for multiline prompts.
  - `jules remote list --session`: Lists all active remote sessions, statuses (`Planning`, `In Progress`, `Completed`, `Failed`), and session IDs.
  - `jules remote pull --session <session_id> [--apply]`: Pulls diff/patch (and optionally applies to local working tree).
  - `jules remote delete --session <session_id>`: Cleans up and deletes a remote session.

## Target Branch Routing Protocol
1. **Target Branch Determination**:
   - Default target branch is `main`.
   - If a specific branch is supplied (e.g., `devin_refactoring`) or if an active working feature branch is checked out:
     - Ensure the target branch is checked out locally and matches origin (`git checkout <target_branch>`).
     - Embed the branch directive directly in the prompt header since `jules remote new` operates on the repository context:
       ```text
       Follow rules in `GEMINI.md`. Work on branch `<target_branch>`.

       <prompt_content>
       ```
2. **Working Tree Cleanliness**: Verify clean git working tree prior to dispatching tasks (`git status`).

## Concurrency & Quota Management (Circuit Breakers)
- **Daily Quota Ceiling**: Maximum 100 tasks per 24-hour rolling window.
- **Concurrent Active Session Limit**: Remote Jules caps active concurrent sessions at **15**. Exceeding this limit returns HTTP 400 (`FAILED_PRECONDITION` / `"Precondition check failed"` / `"all sessions failed to create"`).
- **Batch Tracking File**: `.jules/active_batch.json`
  - Stores active session IDs, submission timestamps, prompt titles, prompt file names, target branches, URLs, and active statuses.
- **Unprocessed Queue File**: `.jules/unprocessed_queue.json`
  - Stores tasks deferred due to daily quota exhaustion, concurrent capacity limits (15 sessions), API rate limits, or network errors.

### Circuit Breaker Procedure
1. Before dispatching each task:
   - Check `.jules/active_batch.json` to calculate tasks submitted in the past 24 hours. If count >= 100, halt dispatch.
2. During task dispatch:
   - If an API error occurs (HTTP 400 precondition failure, HTTP 429 rate limit, quota error, or exit failure):
     a. Immediately halt the batch dispatch loop.
     b. Move the failed task and all remaining pending prompts into `.jules/unprocessed_queue.json` with status `"deferred"` and a detailed reason.
     c. Log a clear warning message indicating circuit breaker trip and summary of queued tasks.

## Dispatch Workflow & Output Parsing

### 1. Preparing the Prompt
- Extract the task title from the first `# User Story: ...` or markdown heading.
- Prefix the prompt text:
  ```text
  Follow rules in `GEMINI.md`. Work on branch `<target_branch>`.
  ```

### 2. CLI Dispatch Execution
Execute the CLI command via stdin piping to preserve multiline formatting:
```bash
# Via shell:
Get-Content prompt.txt -Raw | & "C:\Users\devin\AppData\Roaming\npm\bin\jules.exe" remote new --repo .

# Via Python subprocess:
subprocess.run(
    [r"C:\Users\devin\AppData\Roaming\npm\bin\jules.exe", "remote", "new", "--repo", "."],
    input=full_prompt_text,
    text=True,
    cwd=repo_dir,
    capture_output=True
)
```

### 3. Parsing Response
A successful dispatch produces stdout matching:
```text
Session is created.
ID: <SESSION_ID>
Task: <PROMPT_SUMMARY>
URL: https://jules.google.com/session/<SESSION_ID>
```
Extract:
- `session_id`: RegEx `ID:\s*(\d+)`
- `url`: RegEx `URL:\s*(https?://\S+)`

### 4. Updating Batch Metadata
Record entry in `.jules/active_batch.json`:
```json
{
  "session_id": "8201141252215842247",
  "title": "User Story: In-Game Shroud & Shroud System Overhaul",
  "prompt_file": "01_InGameShroudAndFogOfWarOverhaul.txt",
  "target_branch": "devin_refactoring",
  "url": "https://jules.google.com/session/8201141252215842247",
  "timestamp": "2026-09-10T22:02:34.016300+00:00",
  "status": "active"
}
```

If deferred, record entry in `.jules/unprocessed_queue.json`:
```json
{
  "title": "User Story: Localization Automation & Translation Verification Tooling",
  "prompt_file": "16_LocalizationAndTranslationTooling.txt",
  "target_branch": "devin_refactoring",
  "status": "deferred",
  "reason": "concurrent_session_limit_or_api_error",
  "details": "...",
  "timestamp": "2026-09-10T22:03:52.786714+00:00"
}
```
