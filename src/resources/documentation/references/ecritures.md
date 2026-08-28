# Écritures

**Portée :** modifier le modèle — le verrou, la prévisualisation, les paramètres,
et le cas où un script devient légitime.
**Sources :** `../SECURITY.md`, `src/RiveTT.Core/Security/CodeSandbox.cs`.
**Vérifié le :** 2026-08-28

## Le verrou d'écriture passe avant tout

Chaque session Revit démarre en **lecture seule**. Tout outil capable de modifier le
modèle est refusé par `PermissionDenied` tant qu'un humain n'a pas pressé *Écriture*
dans le panneau RiveTT du ruban (onglet *Compléments*).

Aucun outil ne lève ce verrou, `dryRun` compris. Sur un refus avec
`writesAllowed: false`, s'arrêter et demander le déverrouillage — ne pas réessayer.

## Prévisualiser avant d'écrire

RiveTT est en mode automatique permanent et n'ouvre aucune boîte d'autorisation. La
sûreté vient donc d'ailleurs : prévisualisation explicite, entrées étroites,
transactions Revit, erreurs structurées, vérification après coup.

1. Si l'outil accepte `dryRun`, l'appeler d'abord avec `dryRun: true`.
2. Résumer les comptes et les avertissements importants — ne pas déverser la liste.
3. N'exécuter avec `dryRun: false` que si la demande autorise l'écriture **et** que
   l'aperçu correspond à la portée voulue.
4. Le vrai appel reprend les mêmes entrées, à `dryRun` près.
5. Vérifier le résultat par une lecture ensuite.

**Toute prévisualisation doit porter `mutated: false`.** Son absence est une rupture
de contrat : ne pas enchaîner sur l'écriture réelle.

## Lire ce que la réponse dit vraiment

Un succès n'est pas un résultat utilisable. Plusieurs outils réussissent en
produisant quelque chose d'inexploitable, et le disent :

| Outil | Champ à lire | Ce qu'il révèle |
|---|---|---|
| `create_room` | `enclosed`, `areaM2` | une pièce non fermée a une aire nulle et ne sert à rien |
| `create_sheet` | `hasTitleBlock` | sans cartouche, c'est une feuille A4 nue sans cadre |
| `create_stair` | `reachesTopLevel` | la volée peut ne pas atteindre le niveau visé |
| tous | `warnings`, `notFoundIds`, `unresolvedParameterNames`, `skippedFields`, `cascadedElements` | ce qui a été sauté, et pourquoi |

Après une écriture, lire d'abord ce rapport, avant de relire le modèle. Et une
lecture qui revient avec `execution.cached: true` est une réponse de cache, pas une
observation fraîche.

Pour une transaction complexe, démarrer en `warningPolicy: allow_list` quand les GUID
de `FailureDefinition` acceptables sont connus : un avertissement inattendu provoque
alors un retour arrière au lieu d'être masqué. Un retour arrière expose `warnings`,
`errors`, `failedElementIds` et `repairHints`.

`save_document` et `save_as_document` se prévisualisent aussi : chemins, existence de
la cible, politique d'écrasement, droits sur le dossier, verrous, modifications non
enregistrées — sans rien écrire. Utile avant un « enregistrer sous » de plusieurs
centaines de Mo. Rappel : `save_as_document` **duplique** le document ouvert.

## Paramètres

### Quel outil

| Cas | Outil | Remarque |
|---|---|---|
| 1 élément, 1 à 3 paramètres | `set_element_parameters` | |
| N éléments, même paramètre et même valeur | `batch_modify_parameter_values` | `dryRun` obligatoire d'abord |
| N éléments, valeurs différentes | `sync_csv_parameters` | lignes portant `elementId` ; `parameterMap` pour viser un `BuiltInParameter` |
| Recopier d'un élément à d'autres | `match_element_properties` | toujours avec `parameterNames` explicite |

### Découvrir les noms

Ne jamais supposer le nom d'un paramètre projet. `get_element_parameters` sur **un**
élément témoin donne les noms exacts ; les paramètres de type y sont préfixés
`[Type]`.

Sur un projet localisé, mapper les en-têtes stables vers un `BuiltInParameter`
(`{"Numéro": "ROOM_NUMBER"}`) plutôt que de dépendre du texte affiché.

Pour filtrer sur un paramètre de **type** — le nom du type, par exemple —
`filter_by_parameter_value` avec `parameterType: "type"`. Le défaut `"both"` peut ne
pas résoudre une chaîne de niveau type.

### Garder une portée stable

Entre l'aperçu, la vérification et l'écriture, la sélection ne doit pas bouger. Par
ordre de préférence :

1. `elementIds` explicites ;
2. `capture_selection` et son `selectionToken` temporaire ;
3. `savedSelectionName`, pour une sélection persistée dans le modèle ;
4. `scope: selection`, seulement pour un appel unique et immédiat.

## Escalader vers send_code_to_revit

**Jamais de son propre chef pour une opération en masse.** Toujours demander
l'accord, en proposant l'alternative native comme option A :

> « Je peux utiliser `send_code_to_revit` pour faire ça plus efficacement avec un
> script C#, ou procéder avec les outils dédiés — ce qui demandera plus d'appels.
> Que préférez-vous ? »

Les raisons de demander plutôt que de supposer :

- un script contourne les schémas dédiés, et donc leur `dryRun` ;
- des conflits de DLL avec d'autres add-ins peuvent faire échouer `send_code_to_revit`
  silencieusement ;
- l'utilisateur peut préférer la traçabilité d'appels d'outils distincts.

### Bac à sable

Ces espaces de noms sont refusés par `CodeSandbox.Validate` avec
`CortexErrorCode.PermissionDenied` :

`System.IO` · `System.Net` · `System.Diagnostics.Process` · `Microsoft.Win32` ·
`System.Reflection.Emit` · `System.Runtime.InteropServices`

### Conventions de code

- le document se nomme `document`, jamais `doc` ni `uidoc` ;
- `new UIDocument(document)` pour l'interface ;
- un `ElementId` se lit par `.Value`.

Un `ExternalEvent` est un contexte d'API valide, et moins contraint qu'on ne l'a cru :
changer de document actif et ouvrir un `StairsEditScope` y fonctionnent. Avant de
déclarer une opération impossible, vérifier que la restriction vise bien ce
contexte-là et non un gestionnaire d'événement API ou un éditeur modal.

## À éviter

- Enchaîner `set_element_parameters` en boucle sur N éléments.
- Lancer `batch_modify_parameter_values` sans `dryRun`.
- Lire la liste complète des éléments depuis un `dryRun` : n'en tirer que
  `modifiedCount` et `skippedCount`, et vérifier que `processed` correspond à la
  portée attendue.
- Utiliser `send_code_to_revit` pour contourner un outil dédié ou son `dryRun`.
- Supposer que l'utilisateur préfère un script : c'est l'option B par défaut.
