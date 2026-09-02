# Plan de correctifs — avant release 0.4.0 (build complète, pas un patch)

Ce document est un prompt d'agent : autonome, destiné à être donné tel quel à une
session Claude Code (ou toute autre) pour exécuter les corrections ci-dessous. Il ne
suppose aucune connaissance de la conversation qui l'a produit.

Source : [`docs/recettes/recette-0.4.0-2026-08-31.md`](../recettes/recette-0.4.0-2026-08-31.md),
recette d'utilisation MCP menée sur `RiveTT-Setup-0.4.0.exe` (build du 31/08/2026,
commit plugin `2276548`) contre une maquette réelle (`Fichier test 0.4.0.rvt`, Revit
2027). Blocs 0 à 5 du protocole exécutés — voir le rapport pour le détail complet des
appels, réponses et vérifications indépendantes.

**Décision : ne pas publier `RiveTT-Setup-0.4.0.exe` tel quel.** Les deux défauts
critiques ci-dessous contredisent la promesse même que cette version annonce
(`docs/CHANGELOG_0.4.0.md`, §1 et §3 : verrou d'écriture fiable, `dryRun` qui ne ment
jamais). Il faut une build complète — nouveau tag, nouveau changelog, nouvel
installateur — pas un patch binaire sur l'existant.

---

## À corriger avant la release 0.4.0

### 1. `dryRun` non supporté : le refus `InvalidInput` ne s'applique pas — CRITIQUE

**Preuve.** `add_shared_parameter` déclare `execution.supportsDryRun: false` dans sa
propre réponse. Appelé avec `dryRun: true`, il a répondu `success: true` et **a
réellement créé** un paramètre partagé lié à la catégorie Portes — confirmé par
lecture indépendante (`list_shared_parameters` : 48 → 49 paramètres avant/après).
Reproduit une seconde fois sur `manage_view_display` (conséquence bénigne ici, état
UI seulement, mais même mécanique).

**Contrat rompu.** `get_server_capabilities.dryRun.whenUnsupported` promet : « dryRun
=true is REFUSED with InvalidInput before execution — the tool is never run ». C'est
exactement le scénario que `docs/CHANGELOG_0.4.0.md` §3 dit avoir corrigé pour 56 (puis
100/136) outils. Cette recette prouve que la couverture a un trou, ou qu'une
régression l'a réintroduit depuis.

**À faire :**
- Localiser dans `src/RiveTT.Tools` (ou le routeur, `RiveTTRouter` côté
  `src/RiveTT.Core`) le point où `dryRun: true` est vérifié contre
  `[ToolSafety(supportsDryRun: ...)]` avant exécution. Le changelog affirme que ce
  garde-fou existe et est verrouillé par un test qui balaie tout `RiveTT.Tools`
  (`DryRunDeclarationSourceTests`, cité dans `docs/references/checklist-release.md`).
  Déterminer pourquoi `add_shared_parameter` (et potentiellement d'autres outils
  `supportsDryRun: false`) y échappe.
- Écrire ou étendre un test qui appelle **chaque outil** déclarant
  `supportsDryRun: false` avec `dryRun: true` et vérifie `InvalidInput` avant toute
  transaction Revit — pas seulement une vérification statique de la déclaration
  `[ToolSafety]`, mais un test d'exécution qui aurait attrapé ce cas précis.
- Vérifier en particulier `manage_view_display` et tout autre outil "vue/sélection"
  qui pourrait avoir le même trou (le changelog ne les classe peut-être pas comme
  "écriture destructive" et les a donc exclus du balayage original).

### 2. Désérialisation des paramètres tableau — CRITIQUE, transversal

**Preuve.** Un paramètre de type tableau, omis ou passé comme `[]`, arrive côté
outil comme une **chaîne littérale** (`""` ou `"[]"`) au lieu d'un tableau vide ou
absent, et se fait refuser avec `InvalidInput`. Seul un tableau **non vide**
traverse correctement. Confirmé sur 9 outils :

`get_current_view_elements` (aucun contournement possible — outil inutilisable),
`list_family_types`, `filter_by_parameter_value`, `get_room_openings`,
`measure_between_elements`, `get_elements_in_spatial_volume`,
`export_elements_data`, `manage_scope_boxes`, `manage_area_plans`.

Les deux derniers prouvent que ce n'est pas conditionné à l'écriture : leur action
`action:"list"` — une lecture pure — échoue avant même le contrôle du verrou.

**À faire :**
- C'est un défaut de la couche de désérialisation **partagée** entre la passerelle
  MCP (`src/RiveTT.Server`) et les outils (`src/RiveTT.Tools`), pas 9 défauts
  indépendants. Chercher le point commun : probablement la conversion des arguments
  JSON du protocole MCP vers les paramètres `.NET` des `IRiveTTTool`, où un tableau
  vide ou absent est sérialisé en chaîne avant d'atteindre le validateur.
  `AGENTS.md` documente que les deux moitiés ne s'accordent que par nom de clé JSON
  — c'est un bon point de départ pour situer le code responsable.
- Corriger pour qu'un paramètre tableau omis soit traité comme une liste vide
  logique (pas d'erreur), et qu'un tableau JSON valide soit désérialisé comme
  structure, jamais comme texte.
- Ajouter un test qui balaie tout `IRiveTTTool` déclarant un paramètre de type
  tableau et vérifie qu'il accepte l'omission ET un tableau vide explicite, sur le
  modèle de `DryRunDeclarationSourceTests`.
- Une fois corrigé, revérifier `modify_schedule` (voir point 3) et les 9 outils
  ci-dessus par un rejeu ciblé — pas besoin de refaire toute la recette, seulement
  ces appels précis (déjà documentés dans le rapport avec les paramètres exacts
  utilisés).

### 3. `modify_element` : erreur non structurée — MAJEUR

**Preuve.** `modify_element(action:"move", ...)` a renvoyé
`"An error occurred invoking 'modify_element'."` — aucun `code`, aucun `execution`,
aucun `success:false` structuré. Reproduit 3 fois, verrou fermé et ouvert : le
défaut est indépendant de l'état du verrou, donc pas un symptôme du point 1 ou 2.

**À faire :**
- Reproduire l'appel exact (`elementIds`, `action:"move"`, `translation`) en
  environnement de dev et capturer la stack trace réelle — l'erreur actuelle ne
  donne aucune piste.
- Appliquer le même traitement que le commit récent du dépôt
  (`b7bdb24 Nommer l'outil dans les erreurs fourre-tout, et leur donner une issue`) :
  nommer l'outil dans l'erreur, retourner un contrat structuré, ouvrir une issue si
  la cause est plus profonde qu'un bug de surface.
- Vérifier que `rotate`, `mirror`, `copy` (les 3 autres actions du même outil) ne
  partagent pas la même casse non gérée.

### 4. Étape 0.3 du protocole de recette — jamais éprouvée, doit l'être avant release

Cette recette n'a pas pu observer la page « ATTENTION : mise à jour incomplète »
parce que le serveur n'était jamais réellement périmé au moment du test (l'apparente
péremption initiale était une copie fantôme du serveur dans le conteneur MSIX de
l'application cliente — voir le rapport, section "Correction — mon diagnostic
précédent était faux" — et non un défaut de l'installateur lui-même).

**À faire avant de shipper 0.4.0 :** fabriquer délibérément l'état dégradé (installer
une version antérieure, puis lancer l'installateur 0.4.0 avec le client MCP ouvert,
répondre Oui) et confirmer que la page finale affiche bien l'avertissement. C'est le
test qui compte le plus dans toute l'étape 0 selon le protocole lui-même
(`docs/references/protocole-de-recette.md`, "0.3 est le cas qui compte") et il n'a
toujours pas de preuve positive.

### Une fois les points 1 à 4 traités

- Nouveau build complet (`.\builder\build.ps1`), nouveau
  `docs/CHANGELOG_0.4.0.md` mis à jour ou `CHANGELOG_0.4.1.md` selon le versionnage
  choisi, nouveau tag.
- Rejouer un bloc 3 minimal (les 2 outils `supportsDryRun:false` fautifs + un
  échantillon élargi d'autres outils sans prévisualisation) et le bloc 1 pour les 9
  outils du point 2, sur une maquette fraîche. Ajouter un paragraphe au rapport
  existant ou en ouvrir un nouveau `docs/recettes/recette-0.4.0-<date>-verif.md` —
  ne jamais écraser le rapport du 31/08.

---

## Reporté à 0.5.0 — reste du protocole de recette

Ces points ne bloquent pas la release 0.4.0 corrigée : ce sont des défauts mineurs,
ou de la couverture qui reste à faire, pas des ruptures de la promesse de sécurité
de cette version.

### Défauts mineurs à corriger (sans urgence de release)

- **`find_untagged_elements`, `list_schedulable_fields`** : résolveur de catégorie
  incohérent avec le reste de la surface — refuse le français (`"Portes"`), accepte
  l'anglais (`"Doors"`), alors que `filter_elements`, `list_family_types`,
  `list_system_types` acceptent les deux. Aligner sur le même résolveur.
- **`get_schedule_data`** : la première ligne de `rows` duplique les en-têtes de
  colonnes au lieu d'être la première ligne de données réelle — décalage
  d'indexation probable dans la lecture de la `ViewSchedule`.
- **`save_document`** : l'aperçu (`dryRun:true`) a annoncé un blocage (« locked by
  another process ») qui ne s'est pas matérialisé à l'exécution réelle immédiatement
  après. Moins grave que le point 1 (sens inverse : ici l'aperçu est trop
  pessimiste, pas silencieux sur une vraie écriture), mais nuit à la confiance.
- **`get_element_parameters`** : un nom de paramètre non résolu (`"Type Name"`
  demandé explicitement) retourne une liste vide au lieu de passer par
  `unresolvedParameterNames`, contrairement au contrat documenté.
- **Trois outils `manage_unplaced_views`, `manage_phase_filters`,
  `manage_additional_settings`** classés `toolReadOnly: false` au niveau de l'outil
  entier : leur action `list` (lecture pure) est donc bloquée par le verrou de
  session. Confirmer avec l'équipe produit si c'est voulu ; si non, la classification
  `[ToolSafety]` devrait distinguer par action, pas seulement par outil.

### Couverture à terminer (protocole de recette complet)

La recette du 31/08 a échantillonné ~50 outils sur ~198 recensés dans
`inventaire-des-outils.md` — volontairement partiel, pas une couverture exhaustive.
Pour 0.5.0 :

- **Worksets.** `hasWorksets: false` sur la maquette utilisée (non partagée) : tous
  les outils worksets sont restés `non testé`. Nécessite un modèle central
  (`isWorkshared: true`), à préparer spécifiquement.
- **Fichiers liés.** Un seul lien chargé a été testé en lecture
  (`get_link_transform`, `list_linked_file_instances`) ; les écritures
  (`align_link_to_host`, `move_link_instance`, `pin_unpin_link_instance`,
  `reload_linked_file_from`, `add_linked_file`, `manage_links` en écriture réelle) ne
  l'ont pas été.
- **IFC.** Seul le chemin de lecture a été exercé (`ifc_get_capabilities`,
  `ifc_list_export_configurations`, `ifc_analyze_rebuildability` — 0 résultat, aucun
  DirectShape IFC dans la maquette). Aucun export, import, ni rebuild réel testé.
- **Groupes de modèles, options de conception en écriture.** Lus
  (`list_design_options`), jamais modifiés (`edit_group_members`, ajout/retrait de
  membre, changement d'option active).
- **Nomenclatures en écriture.** `modify_schedule` reste à revérifier une fois le
  point 2 ci-dessus corrigé (son test de refus bloc 2 était invalide).
- **Reste des outils de création géométrique** (escaliers, rampes, garde-corps,
  murs-rideaux, toitures, assemblages) : non touchés par cette recette, à couvrir en
  0.5.0 avec une maquette qui en contient des instances représentatives.

### État de la maquette

`Fichier test 0.4.0.rvt` porte des modifications permanentes de cette recette (+1
mur, +1 paramètre partagé résiduel du défaut n°1, -1 pièce et ses étiquettes,
fichier sauvegardé). Ne pas la réutiliser comme référence "propre" pour 0.5.0 sans
recharger une copie neuve du gabarit décrit dans
`docs/references/protocole-de-recette.md` ("La maquette de recette").
