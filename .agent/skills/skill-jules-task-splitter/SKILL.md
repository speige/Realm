---
name: skill-jules-task-splitter
description: Takes an input prompt and creates separate sub-task prompt files enriched with codebase context for parallel execution with Jules.
---

# Skill: Jules Task Splitter (skill-jules-task-splitter)

## Trigger Conditions
Activates when an input prompt needs to be analyzed, enriched with repository context, and split into multiple standalone sub-task prompt files for parallel micro-task execution with Jules.

## Core Purpose
Decomposes complex, multi-item user stories into independent, single-responsibility sub-tasks. Each generated sub-task prompt file is fully contextualized with relevant code references, eliminating ambiguity and enabling autonomous single-shot execution by remote Jules agents.

## Task Splitting Workflow

### 1. Repository Context Gathering
- Parse the input prompt to identify all key domains, components, systems, and acceptance criteria.
- Use `grep_search` and `find_by_name` across the codebase to locate exact file paths, class names, structs, and methods relevant to the task.
- Read relevant code segments into context to identify dependencies, conventions, and architectural constraints.

### 2. Task Decomposition Rules
- **Sub-Tasks**: Split the parent task into individual sub-tasks
- **Single-Shot Scope**: Ensure each task is small enough to be coded in a single-shot prompt without additional feedback from the user.
- **Remove Ambiguity**: Add any missing technical requirements and include related code filename references and symbol names so further clarification is not needed.
- **Independence**: Sub-tasks should be decoupled wherever possible so they can be worked on concurrently.

### 3. Sub-Task Output Format
Each sub-task must be written to its own sequential file: `{{originalFileNameWithoutExtension}}_jules_prompt_#.txt` (saved in `.jules_prompts/` or the designated output path).

Every generated file must strictly adhere to the following template:

```markdown
Follow rules in `GEMINI.md`. Work on branch `<target_branch>`. Never ask for feedback, always determine the ideal path forward and continue working until the task requirements are completed.

# User Story: [<Parent Story Title>] Subtask <#>: <Subtask Title>

## Context & Scope
<Clear description of what this specific subtask accomplishes, its boundaries, and why it is needed>

## Acceptance Criteria
- <Specific, unambiguous requirement for this subtask>

## Key Source Files & Symbols
- `<Path/To/File.cs>`: <Relevant method, struct, or component>
- `<Path/To/AnotherFile.cs>`: <Relevant references>

## Verification Commands
- `dotnet build Realm.slnx`
```