# RiveTT 0.6.3

Trois corrections sur `load_family` `action: "load_from_open"`, issues du test du 15/10/2026
(`Haworth_Chair_Very-Task1` rechargée dans `E23_005_PRO_001.rvt`, RiveTT 0.6.2.0, Revit 2026).
Version unique pour le plugin ET le serveur (`Directory.Build.props`) : pas de `versionMismatch`
si les deux sont réinstallés ensemble.

> **État : compilé et testé hors Revit, pas vérifié sur Revit.** Voir §5.

## 1. Source active non fermée par `closeSource: true` (priorité haute)

- **Cause** : `LoadFamilyTool.LoadFromOpen` décidait « source active » par `ReferenceEquals(active, source)`,
  aussi bien dans le `dryRun` (`sourceIsActive`) que dans la fermeture. Sur le cas observé la comparaison
  rendait `false` pour la famille pourtant active : aucune réactivation, `Close(false)` directement sur le
  document actif, refus de Revit. Seule hypothèse compatible avec le code ; non reproduite hors Revit.
- **Correctif** (`FamilyReloadSupport`, `LoadFamilyTool.LoadFromOpen`) :
  - identité du document par référence **ou** `PathName` (insensible à la casse), ou `Title` pour deux
    documents jamais enregistrés ;
  - ordre `LoadFamily` → réactivation → fermeture. Réactivation par `UIApplication.OpenAndActivateDocument`
    sur le `PathName` que Revit déclare pour la cible (pas un chemin retapé : lecteur mappé / UNC) ; un
    document supplémentaire apparu après coup est signalé dans `closeWarnings` ;
  - si la source est encore active après l'appel (activation appliquée au retour du handler),
    `DeferredSourceClose` ferme au tick `Idling` suivant, 30 s au plus, sans bloquer :
    `sourceClosePending: true` ;
  - cible jamais enregistrée : seul refus admis, `sourceClosed: false`, raison explicite ;
  - un échec d'activation ou de fermeture ne défait jamais le chargement ;
  - `session.Cache.InvalidateAll()` + `BumpDocumentVersion()`, `activeDocument` relu dans l'UI, et
    `execution.documentTitle` pris sur un indice à usage unique (`documentTitleHint`, lu par
    `RiveTTRouter.GetActiveDocumentTitle`) car la liaison de session ne suit que sur `ViewActivated`.
- **`dryRun`** : `sourceIsActive` exact, `wouldReactivate: <titre cible>`, `wouldCloseSource`,
  `sourceCloseBlockedReason`.

## 2. ElementId de la famille changé au rechargement (priorité moyenne)

- **Cause** : non imputable au code. `LoadFromOpen` appelait déjà `source.LoadFamily(cible, options)` avec
  `overwriteParameterValues = true` (pas de chargement par chemin, pas de suppression/recréation, pas de
  renommage temporaire, `FamilySource.Family`). L'API ne permet pas d'imposer l'Id : le changement
  vient de Revit.
- **Correctif** : instantané avant chargement (Id de famille, Id des types par nom, nombre d'instances),
  puis `previousFamilyId` et `familyIdChanged` dans la réponse de `load_from_open` **et** de `load`
  (avec `familyPath`), plus `familyIdNote` quand l'Id a changé.

## 3. Vérifier la cible sans changer d'actif (priorité basse)

Option B retenue : bloc `verification` lu dans la cible après chargement (`types[]` avec `id`, `name`,
`instanceCount`, `previousId`, `idPreserved` ; `instanceCount`, `instanceCountBefore`,
`instancesPreserved`, `typeIdsPreserved`, `familyCountWithName`). L'option A (énumérer `app.Documents`
en `readonly`) aurait élargi la surface du bac à sable pour un besoin que B couvre. `ReadOnlyScriptAnalyzer`
n'est pas touché.

## 4. Rapport EQS_IRM du 15/10/2026 (famille lourde : 11 390 lignes, groupes de 9 470 et 1 890 membres)

### 4.1 `fromScript` perdait le statut REUSABLE (haute)

- **Cause** : `SendCodeToRevitTool.Execute` lisait `reusable` avec un défaut `false`, et le wrapper MCP
  l'envoyait toujours : une nouvelle version issue de `fromScript` repartait en `TEMP`.
- **Correctif** : `reusable` n'est « énoncé » que s'il est un booléen présent ; avec `fromScript`, le statut est lu
  sur l'en-tête (`// REUSABLE`) de la version exécutée (`ResolveReusable`). Réponse : `scriptLifetimeInherited`.
  Le wrapper n'envoie `reusable` avec `fromScript` que s'il vaut `true`. **Écart avec la demande** : « TEMP seulement
  si `reusable: false` explicite » n'est pas exprimable sur la surface MCP (un `bool?` casse l'appel, test
  `NoMcpToolPublishesANullableBooleanParameter`) ; le déclassement passe par `lifetime: "temp"` (`"reusable"` force
  l'inverse). Côté runtime, un `false` explicite déclasse bien (testé).

### 4.2 Délais sans alternative (haute)

- **Mesure** : impossible hors Revit ; `capture_view` rend désormais `timingsMs` (copie de la vue, `fitToModel`,
  export, lecture) et `fitToModelElementsScanned`. Hypothèse la plus probable : l'export (HLR sur 11 390 courbes)
  et la boucle `get_BoundingBox` de `fitToModel`, deux phases que `ExportImage` / Revit ne laissent pas interrompre.
- **`capture_view`** : délai du dispatcheur 180 s → 90 s ; budget interne : emprise `fitToModel` plafonnée à 40 s,
  export non lancé au-delà de 55 s ; erreur `Timeout` structurée (phase, durées, `pixelSize`, pistes : baisser
  `pixelSize`, `Coarse`, renoncer à `fitToModel`, `bboxMm`), copie temporaire annulée. Un export déjà lancé ne
  s'interrompt pas : seul le délai du dispatcheur (90 s) répond alors.
- **`send_code_to_revit`** : voie **B** retenue. `async: true` → `jobId` immédiat (`RevitThreadDispatcher.StartJob`,
  `JobRegistry`), `get_job_status(jobId)` (nouvel outil, `IInlineTool` : répond hors de l'événement Revit occupé).
  Raison : la patience du client MCP n'est pas réglable depuis RiveTT, un `timeoutMs` de 600 s ne la prolonge pas.
- **Erreur de délai** : dit si l'outil tourne encore (`toolStillRunning`, `jobId`), a fini juste après, ou n'a jamais
  démarré (requête annulée). Le résultat d'un appel dépassé est conservé dans le registre au lieu d'être jeté
  (`completed_after_timeout` reste dans le journal d'audit, ligne `job … finished`). Le registre est en mémoire,
  6 h, 50 tâches. Limite connue : la sérialisation du canal (un client à la fois) ne gêne pas `get_job_status`
  parce que l'appel `async` rend la main aussitôt.

### 4.3 `deletedCount` ventilé (moyenne)

`changes` rend `deletedTopLevelCount`, `deletedAuxiliaryCount`, `deletedUnclassifiedCount`, `deletedByClass`
(15 classes + « autres »). Même règle d'« auxiliaire » qu'à la création (esquisses, courbes, sans catégorie) :
**écart avec l'exemple** de la demande, où `ModelLine` figure parmi les éléments principaux ; ici les lignes sont
comptées auxiliaires et `deletedByClass` les détaille toutes. Les classes sont relevées avant l'exécution
(un élément supprimé ne se lit plus), omises au-delà de 60 000 éléments (`deletedBreakdownAvailable: false`).

### 4.4 Groupes en famille (à diagnostiquer)

- **Non reproduit** : impossible sans Revit, donc **aucune limite d'API n'est inscrite** dans « Ce que l'API Revit ne
  permet pas ». La reproduction hors `readonly` reste à faire (§5).
- `manage_model_groups` : `ungroup` existait déjà (l'affirmation « seul un script dégroupe » est périmée) ; sa réponse
  est désormais bornée (`releasedCount`, 200 ids, `notFoundIds`). Nouvelle action `regroup` (`elementIds`, `newName`,
  `dryRun` par transaction annulée) : `FamilyCreate.NewGroup` en famille, `Create.NewGroup` en projet, remplace un
  type vide du même nom, refus structuré sinon.

### 4.5 Bac à sable et pièges (basse)

Le refus de `dynamic` propose tuples nommés, types anonymes, classe locale, `JObject`. SKILL : `Document.Create` en
famille → `FamilyCreate`, `dynamic`, `NewGroup` sur lignes de modèle. **Le texte de la demande s'arrêtait au piège
`Sketch.GetAllElements()` sur une esquisse sans propriétaire (`OwnerId` invalide)** : ce piège n'est pas documenté,
faute d'en connaître le symptôme mesuré.

## 5. À vérifier sur Revit (2026 ET 2027)

- [ ] Source active + cible enregistrée sur `J:\` (lecteur mappé) : `sourceClosed: true` ou
      `sourceClosePending: true` puis fermeture effective ; pas de second document ouvert
      (`closeWarnings` vide) ; `activeDocument` = titre du projet.
- [ ] Le cas échéant, la fermeture différée par `Idling` aboutit (`Document.Close` y est-il permis ?).
      Sinon, passer par un second `ExternalEvent`.
- [ ] Source active + cible jamais enregistrée : refus avec raison, famille chargée.
- [ ] Source en arrière-plan : fermeture directe inchangée.
- [ ] `dryRun` : `sourceIsActive: true` sur la famille active, `wouldReactivate` renseigné.
- [ ] Rechargement : `familyIdChanged` correspond à la réalité ; Id de types et nombre d'instances
      (34 sur le cas Haworth) conservés dans `verification`.
- [ ] `execution.documentTitle` de la réponse nomme le projet après réactivation.

Tests : `Release063SourceTests` (décisions pures + contrats de source).
- [ ] `fromScript` d'un script `REUSABLE` : `scriptLifetime: "REUSABLE"`, `scriptLifetimeInherited: true` ; `lifetime: "temp"` le déclasse.
- [ ] `send_code_to_revit(async: true)` sur 9 470 éléments : `jobId` immédiat, `get_job_status` répond pendant l'exécution.
- [ ] Appel synchrone dépassant 120 s : erreur avec `toolStillRunning` et `jobId`, puis résultat via `get_job_status`.
- [ ] Le canal tubulaire (un client à la fois) laisse passer `get_job_status` pendant un job.
- [ ] `capture_view(fitToModel, pixelSize: 1400)` sur EQS_IRM : durées de `timingsMs`, ou `Timeout` structuré en moins de 90 s.
- [ ] `deletedByClass` sur un dégroupage de 9 470 membres ; temps de relevé des classes sur une famille de 12 000 éléments.
- [ ] `manage_model_groups regroup` en `transactionMode` de l'outil (pas `readonly`), famille ET projet : le refus
      « None of the input elements can be grouped » se reproduit-il hors bac à sable ? Si oui, l'inscrire dans la section
      « Ce que l'API Revit ne permet pas » (`tools/audit-tool-surface.py`, ligne ~344).
