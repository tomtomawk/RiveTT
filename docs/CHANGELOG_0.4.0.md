# Changelog — RiveTT 0.4.0

0.3.0 a consolidé la surface d'outils. 0.4.0 porte deux choses : le **verrou d'écriture**
au ruban, et la fin d'une série de promesses que le code ne tenait pas. La seconde vient
d'un audit général du dépôt (31/08/2026) dont les mesures sont citées ici plutôt que
résumées.

`src/resources/documentation/references/inventaire-des-outils.md`, généré par
`tools/audit-tool-surface.py`, reste la source de vérité vivante sur la surface.

---

## 1. Le verrou d'écriture

Chaque session Revit démarre en **lecture seule**. `RiveTTRouter.Route` refuse tout outil
classé `readOnly: false` avec `PermissionDenied`, avant le cache et avant le contrôle de
document ouvert, jusqu'à ce qu'un humain presse *Écriture* dans le panneau **RiveTT** du
ruban (onglet Compléments).

- aucun outil ne peut lever le verrou : `WriteAccessGateTests` balaie `RiveTT.Tools` et
  échoue si l'un appelle `WriteAccessPolicy.Set` ;
- `dryRun: true` n'est pas une exemption ;
- le verrou est un état de **session** : il survit à l'ouverture, la fermeture et
  l'enregistrement sous d'un document.

`[ToolSafety(readOnly, …)]` cesse d'être de la métadonnée et devient une frontière de
permission. Conséquence directe : un outil mal classé traverse le verrou — voir §3.

## 2. Les deux moitiés se déclarent séparément

Le plugin (dans Revit) et le serveur MCP (dans `%LOCALAPPDATA%`) s'installent à deux
endroits, et une mise à jour peut n'en poser qu'un. C'est arrivé le 28/08 : le plugin
0.4.0 installé, le serveur 0.2.0 encore en mémoire, publiant des noms d'outils
antérieurs à 0.3.0.

Chaque moitié rapporte donc sa propre version — `execution.pluginVersion`,
`execution.mcpServerVersion` — et le serveur signale le désaccord par
`execution.versionMismatch`. L'installateur refuse de démarrer si
`RiveTT.Server.exe` tourne encore, et vérifie en fin de parcours que le serveur porte
bien la version installée.

## 3. Défauts trouvés par l'audit du 31/08

### `dryRun` : le routeur affirmait une prévisualisation à la place de l'outil — **majeur**

`EnrichResult` tamponnait `dryRun: true, mutated: false` sur la foi de la **demande du
client**, sans consulter l'outil. Pour les 79 outils d'écriture qui ne lisent pas
`dryRun`, la séquence était : l'agent demande un aperçu, l'outil s'exécute et écrit dans
la maquette en transaction, et la réponse dit que rien n'a changé. Le SKILL livré
instruisait précisément l'agent de prévisualiser, et `get_server_capabilities` publiait
`dryRunDefault: true` pour toute la surface — les trois pièces s'alignaient pour produire
l'erreur.

Corrigé en trois temps :

- `supportsDryRun` rejoint `[ToolSafety]`, déclaré sur les **56** outils qui lisent
  réellement le drapeau ;
- le routeur **refuse** `dryRun: true` sur un outil qui ne le déclare pas, avec
  `InvalidInput`, **avant** exécution. Un agent qui demande un aperçu n'obtient jamais
  une mutation ;
- `execution.supportsDryRun` dans chaque réponse, et `get_server_capabilities` publie la
  couverture **comptée** (56 / 135) au lieu d'un booléen global.

`DryRunGateTests` verrouille le refus et la non-exécution ;
`DryRunDeclarationSourceTests` verrouille la correspondance déclaration ⇄ comportement
dans les deux sens, par balayage de tout `RiveTT.Tools`.

Les **14 écritures destructives** qui n'avaient aucune prévisualisation en ont une :
`create_grid`, `change_element_type`, `match_element_properties`, `manage_worksets`,
`manage_links`, `manage_view_templates`, `clean_cad_links`, `create_placeholder_sheets`,
`duplicate_system_type`, `manage_global_parameters`, `modify_schedule`,
`ifc_open_or_import`, `ifc_reload_link`, `ifc_tag_unreconstructable_elements`. **Il ne
reste aucune écriture destructive sans aperçu.**

Elles ne devinent pas. `ChangePreview` généralise ce que `DeletionPreview` faisait déjà
pour les suppressions : l'opération **s'exécute réellement** dans la transaction, puis
elle est **annulée**. Ce qui revient est ce que Revit a fait, objections comprises, sur un
modèle intact. Un aperçu écrit à la main est une seconde implémentation de l'outil, et
c'est elle — pas l'opération — que l'appelant finit par croire.

Deux limites, écrites dans la réponse plutôt que tues :

- un aperçu par sonde ne publie **pas d'identifiants** : ceux alloués pendant la sonde
  sont libérés par l'annulation, et l'appel réel en attribuera d'autres ;
- quand l'effet porte sur un fichier ou un autre document (`ifc_open_or_import`,
  `ifc_reload_link`, `manage_links` en reload), aucune annulation ne le déferait :
  l'aperçu est alors `previewMethod: "declared"`, il rapporte la cible résolue et les
  préconditions vérifiables, et une liste `blockers` vide ne veut pas dire « ça marchera ».

Un outil qui déclare `supportsDryRun` doit l'honorer sur **toutes** ses actions, sinon le
défaut revient par une branche : les huit actions d'écriture de `manage_global_parameters`
passent par une garde centrale, et les quatre de `manage_links` sont couvertes une à une.

Reste ouvert : **66 écritures sur 139**, toutes non destructives — des créations et des
changements de réglage. Ordre de traitement : l'intérêt 5 (16 outils).

### `rename_views` et `manage_unplaced_views` ne pouvaient pas être appliqués — **majeur**

Les deux prévisualisent par défaut (`ToolHelpers.GetDryRun` vaut `true` sans argument), et
leur façade MCP ne publiait pas `dryRun` du tout. Un appelant n'avait donc aucun moyen de
passer à l'exécution : les deux outils ne savaient que prévisualiser, en silence.

Trouvé par le test ajouté pour l'occasion,
`PublishingDryRun_AndDeclaringIt_AgreeAcrossTheTwoHalves`, qui croise les deux moitiés
dans les deux sens. `dryRun` figure parmi les `StructuralKeys` de
`ServerRuntimeParameterContractTests`, qui ne pouvait donc pas le voir. Le même test a
intercepté l'erreur symétrique pendant ce chantier : `modify_schedule` avait reçu `dryRun`
côté façade avant que le runtime ne le déclare, ce qui aurait fait **refuser tous ses
appels** par le routeur.

### `export_schedule` écrivait un fichier arbitraire depuis une session verrouillée — **majeur**

L'outil écrivait `exportPath` par `File.WriteAllText` sans passer par `PathSafety`, et il
était classé `[ToolSafety(true, false)]` — lecture seule — donc le verrou du ruban ne le
voyait pas. Verrou fermé, un appelant pouvait écraser n'importe quel fichier accessible à
l'utilisateur. Même défaut que `ifc_set_family_mapping_file`, reclassé en 0.3.0 pour la
même raison.

Reclassé `[ToolSafety(false, false)]`, chemin filtré, et `overwrite: false` par défaut.

Le point de fond, désormais écrit dans `docs/references/securite-et-audit.md` : **le
verrou d'écriture protège la maquette, pas le disque.**

### Six outils à chemin échappaient à `PathSafety` — **majeur**

`export_schedule`, `save_as_document`, `create_document`, `open_document`, `open_family`,
`open_template`, `load_family`, `manage_images` lisaient un chemin fourni par l'appelant
et l'utilisaient tel quel.

La cause était le test lui-même : `PathSafetySourceTests` était une liste `[InlineData]`
de 17 outils, **énumérative**. Un outil ajouté n'y entrait pas, et son absence n'échouait
rien — le mécanisme que le dépôt avait déjà condamné pour la section « Fonctions
ajoutées » du README. Le test **balaie** maintenant `RiveTT.Tools` et exige la garde ; les
exemptions sont nommées avec leur raison.

### `PathSafety` refusait le lecteur projet de l'agence — **majeur**

Sa liste blanche était Documents / Bureau / Téléchargements / profil / temp. Mesuré :
`P:\Projets\2026-047\...` **refusé**, `\\srv-fichiers\...` refusé pour les exports (un
test l'interdisait explicitement : `ExportTool_StaysStrict_NoUncAllowance`), `D:\` refusé.
Autrement dit : impossible d'exporter dans l'affaire.

Politique inversée en **liste noire** : Windows, System32, Program Files, ProgramData, et
`%LOCALAPPDATA%\RiveTT` lui-même — le journal d'audit est une preuve, un outil capable de
l'écraser pourrait effacer sa propre trace. Tout le reste passe, lecteurs mappés et
partages réseau compris. La traversée `..` est réduite **avant** le contrôle, donc
inopérante.

Un contrôle qui bloque le geste quotidien finit contourné, pas respecté.

### Écraser un fichier n'était pas distingué de le créer — **mineur**

`export_schedule` et `export_to_excel` remplaçaient un fichier existant sans un mot.
`PathSafety.CanWriteTo` refuse désormais une cible existante sauf `overwrite: true`, et la
réponse porte `overwroteExistingFile`.

### Le bac à sable Roslyn était contournable par échappement Unicode — **mineur**

La norme C# autorise les séquences `\uXXXX` dans les **identifiants**. Mesuré le 31/08 :

    System.\u0049O.F\u0069le.WriteAllText(p, s);   →  AUTORISÉ

C'est du C# valide que le compilateur lit `System.IO.File.WriteAllText`, et que le
filtre lisait ni comme `System.IO` ni comme `File.`. Les échappements sont maintenant
décodés avant tout appariement (`CodeSandboxUnicodeEscapeTests`), et les deux formes
démontrées sont bloquées.

Classé mineur, et non majeur, parce que `send_code_to_revit` est derrière le verrou
d'écriture **et** en `dryRun` par défaut : le bac à sable est la quatrième ligne de
défense, pas la première. Ce qui a changé de plus important, c'est la documentation : le
README livré et la référence interne promettaient un blocage des accès fichiers. Elles
disent maintenant ce que le filtre est — un garde-fou contre l'erreur — et ce qu'il n'est
pas — une frontière de sécurité, l'API Revit autorisée écrivant elle-même sur disque
(`Document.SaveAs`) et supprimant des éléments (`Document.Delete`).

### `docs/references/securite-et-audit.md` contredisait le produit — **mineur**

Le document affirmait « there is no `readOnlyMode` setting — any guidance mentioning one
is stale » et « `writesAllowed` (always true) », alors que `get_server_capabilities`
retourne `readOnlyModeExists = true` et que le verrou ferme chaque session au démarrage.
Seul document du dépôt à nier la fonctionnalité phare de cette version. Réécrit.

### La réponse était sérialisée quatre fois par appel — **mineur**

`EnrichResult`, `EstimateResponseBytes`, `EstimateElementsAffected` et
`BuildOutputSummary` reconstruisaient chacun l'arbre JSON depuis `result.Data`. Sur une
réponse `export_elements_data` de quelques Mo : quatre passes complètes pour en rendre
une. Le `JObject` est produit une fois et passé aux trois autres.

### Le test de contrat avait une échappatoire globale — **mineur**

`ServerRuntimeParameterContractTests` versait les clés lues par **n'importe quel** helper
dans un ensemble unique appliqué aux 198 outils : une clé lue par un seul helper comptait
comme lue par tous. La garantie se dégradait à mesure que les helpers grossissaient. Les
lectures d'un helper ne comptent plus que pour les outils dont la source le nomme, et le
test reste vert — aucun paramètre publié ne dépendait de l'union globale.

Les dix « signaux » de l'inventaire sont **inchangés** : ils viennent de l'heuristique
propre à `tools/audit-tool-surface.py`, pas de ce test. Les résoudre demanderait de
suivre la délégation dans le script aussi ; ils restent des signaux à vérifier, ce que
leur libellé annonce déjà.

### Un ruban en échec laissait la session verrouillée, en silence — **mineur**

`RiveTTApp.OnStartup` avale l'exception de construction du ruban dans un
`Trace.WriteLine` — invisible dans Revit sans débogueur. Or le ruban **porte** le verrou :
sans lui, la session est en lecture seule définitivement et rien ne le dit. La cause est
écrite dans `%LOCALAPPDATA%\RiveTT\startup.errors.log`, avec sa conséquence en clair —
même remède que le fichier frère du journal d'audit.

## 4. Intégration continue

Le dépôt est sur GitHub sans build automatique : la suite ne tournait que sur le poste du
mainteneur. `.github/workflows/build.yml` compile **les deux cibles Revit** (les gardes
`#if REVIT2027_OR_GREATER` sont à la compilation : un build 2027 ne prouve rien du 2026),
lance les tests, et échoue si régénérer l'inventaire produit un diff — le document livré
doit décrire la surface livrée.

Sans Revit sur le runner, les tests typés Revit se signalent en *Skip* propre : un rouge
est un vrai rouge.

## 5. Recette sur maquette

`docs/references/protocole-de-recette.md` : protocole à deux agents — un opérateur qui
appelle et consigne, un auditeur qui conteste ligne à ligne en relisant la maquette et
`audit.jsonl`. C'est ce qui manquait pour les 131 outils qu'aucun test ne cite, et pour
tout ce que le paquet NuGet de référence ne peut pas exercer.

## 6. Signature du code

`builder\build.ps1` signe binaires, installateur et désinstalleur par empreinte de
certificat (`RIVETT_SIGN_THUMBPRINT`). La signature est **facultative** : sans certificat
le build passe et avertit. Le certificat produit par `New-SigningCertificate.ps1` est
auto-signé — il ne vaut que sur les postes où le `.cer` a été déployé par GPO. Pour une
diffusion externe, une véritable autorité est nécessaire.

---

## Ce qui reste ouvert

| Sujet | État |
|---|---|
| 66 écritures sans `dryRun`, **aucune destructive** | 16 en intérêt 5 ; ce sont des créations et des réglages, le contrat ne ment plus |
| 128 erreurs `Failed: …` sans suggestion | mineur systémique, corrigé au fil des passages |
| 131 outils cités dans aucun test | c'est l'objet du protocole de recette |
| Session Revit choisie implicitement (la plus récente) | deux instances ouvertes : l'agent écrit dans l'une sans le dire |
| 15 outils à géométrie par boîte englobante | listés dans l'inventaire |
| Vérifications sur maquette du 0.3.0 | `docs/CHANGELOG_0.3.0.md` §6 |
