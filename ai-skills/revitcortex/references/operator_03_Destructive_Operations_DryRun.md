# Destructive operations and dry-run

MCPRVTT27 is permanently automatic and never opens an authorization dialog.
Safety therefore comes from explicit preview, narrow inputs, Revit
transactions, structured errors, validation, and post-write verification.

## Decision rules

1. If a tool supports `dryRun`, call it first with `dryRun: true`.
2. Summarize counts and important warnings without flooding the context.
3. Execute with `dryRun: false` only when the user's request authorizes the
   write and the preview matches the intended scope.
4. Verify the result with a read tool after execution.
5. Do not use `send_code_to_revit` to bypass a dedicated tool or dry-run.
6. Require `mutated: false` in every preview response. Treat its absence as a
   contract violation and do not proceed with the real write.
7. For complex transactions, start with `warningPolicy: allow_list` when the
   acceptable Autodesk FailureDefinition GUIDs are known; unknown warnings then
   trigger a rollback instead of being hidden.

## Checks

- Input IDs, categories, paths, types, and levels are explicit.
- Preview scope matches the request.
- The real call uses the same inputs except for `dryRun`.
- Transaction failures and partial skips are reported.
- Rollbacks expose `warnings`, `errors`, `failedElementIds` and `repairHints`.
- The final state is verified.
