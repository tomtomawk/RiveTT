# Safety metadata, audit log, and sandbox

## Tool safety metadata

`[ToolSafety(readOnly, destructive)]` describes a tool for routing, auditing,
and agents. It is metadata in MCPRVTT27; there is no settings-based read-only
profile. Keep names and attributes aligned.

## Audit

Every routed call is appended to
`%LOCALAPPDATA%\MCPRVTT27\audit.jsonl`. Preserve the router-wide backstop so
successes and failures are both logged, including duration and response size.

## `send_code_to_revit`

`CodeSandbox.Validate` must run before compilation. It blocks filesystem and
network access, process spawning, registry access, native interop, reflection
emit, and related bypasses. The tool remains a last resort even though
MCPRVTT27 has no confirmation dialogs.

## Checks

- Safety attribute matches actual behavior.
- Every exception becomes a structured failure.
- Audit logging remains best-effort and non-crashing.
- Sandbox hardening tests remain enabled.
- Dedicated tools are preferred over arbitrary code.
