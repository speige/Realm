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
   - If a specific branch is supplied or if an active working feature branch is checked out:
     - Ensure the target branch is checked out locally and matches origin (`git checkout <target_branch>`).
     - Embed the branch directive directly in the prompt header since `jules remote new` operates on the repository context:
       ```text
       Follow rules in `GEMINI.md`. Work on branch `<target_branch>`. Never ask for feedback, always determine the ideal path forward and continue working until the task requirements are completed.

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

### 1. Preparing the Prompt File(s)
- Take the entire input prompt from the user
- Prefix it with this text:
  ```text
  Follow rules in `GEMINI.md`. Work on branch `<target_branch>`. Never ask for feedback, always determine the ideal path forward and continue working until the task requirements are completed.
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

## Handling "Awaiting User Feedback" & Remote API Interactions

When `jules remote list --session` reports status `Awaiting User Feedback` (or `AWAITING_USER_FEEDBACK`):

### 1. Authentication Token Retrieval (Windows)
Jules CLI stores OAuth tokens in Windows Credential Manager under `jules-cli:default`.
Running `jules remote list --session` automatically refreshes the token if expired.

To read the token via Python:
```python
import ctypes, json
from ctypes import wintypes

class CREDENTIAL(ctypes.Structure):
    _fields_ = [
        ('Flags', wintypes.DWORD), ('Type', wintypes.DWORD),
        ('TargetName', wintypes.LPWSTR), ('Comment', wintypes.LPWSTR),
        ('LastWritten', wintypes.FILETIME), ('CredentialBlobSize', wintypes.DWORD),
        ('CredentialBlob', ctypes.POINTER(ctypes.c_byte)), ('Persist', wintypes.DWORD),
        ('AttributeCount', wintypes.DWORD), ('Attributes', ctypes.c_void_p),
        ('TargetAlias', wintypes.LPWSTR), ('UserName', wintypes.LPWSTR)
    ]

advapi32 = ctypes.windll.advapi32
CredRead = advapi32.CredReadW
CredRead.argtypes = [wintypes.LPWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(ctypes.POINTER(CREDENTIAL))]
CredRead.restype = wintypes.BOOL
CredFree = advapi32.CredFree
CredFree.argtypes = [ctypes.c_void_p]

def get_jules_token():
    cred_ptr = ctypes.POINTER(CREDENTIAL)()
    if CredRead("jules-cli:default", 1, 0, ctypes.byref(cred_ptr)):
        blob = ctypes.string_at(cred_ptr.contents.CredentialBlob, cred_ptr.contents.CredentialBlobSize)
        CredFree(cred_ptr)
        return json.loads(blob.decode('utf-8')).get("access_token")
    return None
```

### 2. Inspecting Agent Inquiry / Messages
Fetch task activities to read the agent's pending question:
- **Endpoint**: `GET https://aida.googleapis.com/v1/swebot/tasks/{task_id}/activities`
- **Headers**: `Authorization: Bearer <access_token>`, `Content-Type: application/json`
- **Inquiry Extraction**: Find `activitySteps` where `agentActivity.agentMessaged.requiresUserResponse == true` and extract `agentActivity.agentMessaged.text`.

### 3. Sending Response / Feedback to Resume Execution
Send feedback to allow the agent to unblock and continue processing autonomously:
- **Endpoint**: `POST https://aida.googleapis.com/v1/swebot/tasks/{task_id}:interact`
- **Headers**: `Authorization: Bearer <access_token>`, `Content-Type: application/json`
- **JSON Payload**:
  ```json
  {
    "taskId": "<task_id>",
    "userActivity": {
      "feedbackGiven": {
        "feedback": "Please proceed with your plan... Follow GEMINI.md. Never ask for feedback, always determine the ideal path forward and continue working until the task requirements are completed."
      }
    }
  }
  ```

### 4. Remote Session Deletion (API Endpoint)
To delete a remote session directly:
- **Endpoint**: `POST https://aida.googleapis.com/v1/swebot/tasks:delete`
- **Headers**: `Authorization: Bearer <access_token>`, `Content-Type: application/json`
- **JSON Payload**:
  ```json
  {
    "taskIds": ["<task_id>"]
  }
  ```

