# Contrats et erreurs

**Portée :** enveloppe de réponse unifiée de tous les outils RiveTT.
**Sources :** `src/RiveTT.Core/Results/RiveTTResult.cs`.
**Vérifié le :** 2026-05-25

## Regola fondamentale

Ogni tool ritorna `RiveTTResult<T>`. **Mai** lanciare eccezioni o ritornare stringhe raw.

## Success

```csharp
return RiveTTResult<object>.Ok(new {
    greeting = "Hello",
    count = 42
});
```

## Failure

```csharp
return RiveTTResult<object>.Fail(
    RiveTTErrorCode.ElementNotFound,
    "Element 12345 does not exist in the active document",
    suggestion: "Check the element ID or ensure the correct document is open");
```

## Error codes

| Code | Numerico | Quando |
|---|---|---|
| `ElementNotFound` | 100 | ID non esiste, elemento eliminato |
| `PermissionDenied` | 200 | Sandbox or operation denied |
| `TransactionFailed` | 300 | Transaction commit failed |
| `InvalidInput` | 400 | Parametri input malformati |
| `Timeout` | 500 | Operation > timeout limit |
| `Cancelled` | 600 | Utente ha annullato (TaskDialog) |
| `Unknown` | 900 | Eccezione non classificata |

## Propagazione errori dal Plugin al Server

`RevitPipeBridge` trasforma `RiveTTResult.Fail` in un payload JSON strutturato senza rilanciare eccezioni.

Esempio payload:
```json
{
  "success": false,
  "error": {
    "code": "Cancelled",
    "message": "Operation cancelled by user",
    "suggestion": null
  }
}
```

## Required checks

- [ ] Tool ritorna `RiveTTResult<T>` sempre.
- [ ] Errori usano `RiveTTErrorCode` enum, mai stringhe libere.
- [ ] `suggestion` compilato quando utile per l'utente.
- [ ] Nessun `throw` non gestito da `Execute`.
- [ ] Gli errori `TransactionFailed` espongono `warnings`, `errors`,
      `rolledBack`, `failedElementIds` e `repairHints` nel context.

## Avoid

- Non lanciare eccezioni che escono da `Execute`.
- Non ritornare stringhe raw o JObject sciolti.
- Non inventare error codes oltre quelli enum.
- Non lasciare `suggestion: null` se c'è una azione utile per l'utente.
