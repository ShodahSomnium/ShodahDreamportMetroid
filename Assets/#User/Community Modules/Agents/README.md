# Agents — context for AI coding assistants

`WorldCreatorContext.md` gives an AI coding agent (Claude Code, Codex, Cursor, Copilot…) what it
needs to know to help you build a Somnium Space world: platform rules, the Somnium Bridge,
networking, the client's behaviour, testing and the upload checklist.

## How to use it
1. **Copy** `WorldCreatorContext.md` to the root of your Unity project (the folder that contains
   `Assets/`). Do not use it from here: Community Modules updates overwrite this folder, and the
   agent writes notes about *your* world in its last section ("This world").
2. Name the copy after the file your agent reads automatically:
   - Claude Code: `CLAUDE.md`
   - Codex, Cursor and most other agents: `AGENTS.md`
   - Using both: keep `AGENTS.md`, and make `CLAUDE.md` a single line: `@AGENTS.md`
3. Start your agent from the project root.

## After a Community Modules update
Compare your copy with the new `WorldCreatorContext.md` (or ask your agent to merge the changes),
and keep your "This world" section. The version line at the top of the file tells you which one you have.

The file is a prototype and the platform keeps evolving: when it disagrees with the project's
code or logs, trust the project.
