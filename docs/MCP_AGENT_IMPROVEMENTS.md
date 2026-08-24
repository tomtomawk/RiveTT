# Campagne de tests manuels RiveTT — Revit 2027

> Journal de campagne. Le traitement de chaque anomalie (cause racine réelle,
> correction, test de non-régression) est consigné dans
> [MCP_AGENT_FIXES.md](MCP_AGENT_FIXES.md).

## 2026-08-20 — Initialisation de session

### `get_server_capabilities`

- **Scénario :** interroger le contrat effectif du serveur au début de la campagne.
- **Résultat attendu :** obtenir les capacités, le mode d'exécution, le statut du document et les limitations connues.
- **Résultat obtenu :** succès ; RiveTT 0.1.0.0, Revit 2027, exécution automatique, `dryRunDefault: true`, locale `fr`, phases et modèle lié détectés. La réponse porte `execution.readOnly: true` parce que `get_server_capabilities` est lui-même un outil de lecture.
- **Anomalie :** le nom `execution.readOnly` laisse croire à un mode global du serveur, alors qu'il décrit la classification de sécurité de l'outil courant. Cette ambiguïté a conduit à interpréter initialement la session comme verrouillée en lecture seule.
- **Amélioration proposée :** renommer ce champ en `toolReadOnly` ou `operationReadOnly`. Exposer séparément un éventuel état global avec un champ explicite tel que `writesAllowed` et sa raison.

### `get_project_info`

- **Scénario :** lire les informations complètes du projet juste après les capacités du serveur, avec niveaux, phases, sous-projets et liens inclus.
- **Résultat attendu :** obtenir l'identité du projet, son contexte de collaboration, ses phases, niveaux et liens sans modifier le modèle.
- **Résultat obtenu :** succès ; modèle non collaboratif, 3 phases, 13 niveaux et 1 lien Revit non chargé. Les noms et métadonnées confirment un contexte français.
- **Anomalie :** aucune anomalie fonctionnelle observée. Le lien est correctement signalé comme non chargé avec son chemin.
- **Amélioration proposée :** aucune amélioration prioritaire à ce stade ; conserver les options d'inclusion pour limiter les réponses lors des appels suivants.

## 2026-08-20 — Changement de document actif

### `open_document` — outil non exposé

- **Scénario :** ouvrir `C:\Users\theba\Desktop\Saint-Malo_avenue aristide briand_46.rvt`, correspondant au document actif sans le suffixe `_V2`.
- **Résultat attendu :** ouvrir et activer ce projet Revit via RiveTT, puis confirmer son chemin avec `get_project_info`.
- **Résultat obtenu :** impossible à exécuter ; aucun outil RiveTT d'ouverture de document Revit n'est exposé. `get_server_capabilities` précise que `open_document` n'est pas disponible dans le gestionnaire `ExternalEvent`. La lecture de contrôle confirme que le document actif reste `Saint-Malo_avenue aristide briand_46_V2.rvt`.
- **Anomalie :** limitation fonctionnelle bloquante pour les campagnes multi-fichiers ; RiveTT peut enregistrer un document et ouvrir/importer un IFC, mais ne peut pas ouvrir un autre fichier `.rvt`.
- **Amélioration proposée :** fournir un orchestrateur dédié au cycle de vie des documents, avec un outil `open_document` acceptant un chemin absolu et renvoyant clairement l'état d'ouverture/activation. Prévoir la gestion d'un document actif modifié et des éventuels dialogues Revit.

## 2026-08-20 — Liste des pièces du RDC

### `export_elements_data` puis `export_room_data`

- **Scénario :** lister les pièces de `OST_Rooms` dont le niveau est `RDC`, avec numéro, nom et surface.
- **Résultat attendu :** obtenir directement la liste filtrée des pièces du RDC.
- **Résultat obtenu :** `export_elements_data` retourne 0 résultat sur 138 pièces avec le filtre `Niveau equals RDC`. L'outil spécialisé `export_room_data` retourne les 138 pièces et permet d'identifier 22 pièces dont le champ structuré `level` vaut `RDC`.
- **Anomalie :** incohérence de filtrage sur le paramètre de niveau des pièces : le filtre générique ne reconnaît pas une valeur pourtant exposée par l'outil spécialisé.
- **Amélioration proposée :** corriger le filtrage de `export_elements_data` pour les paramètres de niveau intégrés, idéalement via un identifiant de paramètre indépendant de la langue. Ajouter aussi un filtre `levelName` natif à `export_room_data` afin d'éviter de retourner toutes les pièces du modèle.

## 2026-08-20 — Enregistrement en version V3

### `save_as_document`

- **Scénario :** enregistrer le document actif sous `C:\Users\theba\Desktop\Saint-Malo_avenue aristide briand_46_V3.rvt`, sans écraser de fichier existant.
- **Résultat attendu :** prévisualiser l'opération avec `dryRun: true`, effectuer ensuite la sauvegarde réelle, puis confirmer le nouveau chemin avec `get_project_info`.
- **Résultat obtenu :** opération non exécutée lors de ce premier essai. La cible V3 n'existe pas, mais le schéma de `save_as_document` n'expose pas de paramètre `dryRun`. Le champ `execution.readOnly: true` observé sur les outils de contrôle décrit leur classification en lecture seule et non un verrou global. La lecture de contrôle confirme alors que le document actif reste le fichier `_V2.rvt`.
- **Anomalie :** une écriture de cycle de vie potentiellement coûteuse ne permet pas de respecter le workflow obligatoire d'aperçu. Le contrat global annonce `dryRunDefault: true`, alors que cet outil ne fournit aucune option de prévisualisation.
- **Amélioration proposée :** ajouter `dryRun` à `save_as_document` et retourner au minimum le chemin source, le chemin cible, l'existence de la cible, la politique d'écrasement et les blocages prévisibles. Clarifier également dès l'aperçu si le mode lecture seule interdira l'exécution réelle.

## 2026-08-20 — Enregistrement V3 sans dry-run, sur autorisation explicite

### `save_as_document` et vérification par `get_project_info`

- **Scénario :** approfondir le test en exécutant directement `save_as_document` vers `C:\Users\theba\Desktop\Saint-Malo_avenue aristide briand_46_V3.rvt` avec `overwrite: false`, malgré l'absence de `dryRun`.
- **Résultat attendu :** créer la V3, activer le nouveau chemin dans le document courant, puis obtenir ce chemin avec `get_project_info`.
- **Résultat obtenu :** `save_as_document` réussit en 13,281 s et renvoie le chemin et le titre V3 avec `execution.readOnly: false`. Le fichier V3 existe sur disque, avec une taille de 216 424 448 octets. L'appel de contrôle `get_project_info` avec la signature déjà utilisée renvoie immédiatement le chemin V2 en 0 ms ; une nouvelle signature de lecture, absente du cache, confirme ensuite le chemin actif V3.
- **Anomalie :** la sauvegarde est effective, mais la lecture de contrôle retourne une valeur obsolète. `get_project_info` utilise le cache de portée `Document`, alors que l'événement de sauvegarde n'invalide que le cache de portée `Transaction`. Un Save As modifie `Document.PathName` sans déclencher l'invalidation attendue ; l'audit confirme que la réponse V2 a été servie en 0 ms.
- **Amélioration proposée :** invalider le cache de `get_project_info` après `save_as_document`, notamment sur l'événement Revit de Save As, ou ne pas mettre en cache les champs de cycle de vie tels que `Document.PathName` et `Document.Title`. Ajouter un indicateur `cached` aux réponses pour rendre ce comportement visible.

### Sémantique de `execution.readOnly`

- **Scénario :** déterminer l'origine de `execution.readOnly: true` et vérifier s'il s'agit d'un mode global modifiable.
- **Résultat attendu :** identifier clairement la source du réglage et la possibilité de l'autoriser.
- **Résultat obtenu :** le champ est produit par `CortexRouter.IsToolReadOnly`/`IsToolReadOnly` à partir de `[ToolSafety]` ou des préfixes de noms. Les outils `get_*` sont marqués `true`, tandis que `save_as_document` est marqué `false`. Le fichier utilisateur `C:\Users\theba\.rivett\settings.json` ne contient que `DisabledTools` et `EnableCodeExecution`, sans option `ReadOnlyMode`.
- **Anomalie :** le guide RiveTT générique mentionne un ancien ou autre mécanisme `readOnlyMode`, absent de RiveTT, et le champ de réponse actuel est ambigu.
- **Amélioration proposée :** aligner la documentation sur RiveTT et distinguer explicitement la classification de l'outil d'une permission globale d'écriture.

## 2026-08-20 — Suppression du carré de murs dans la cafétéria

### `get_elements_in_spatial_volume`, `get_element_solid_geometry` et `delete_element`

- **Scénario :** identifier puis supprimer les murs formant un carré dans la pièce `1308 - Cafétéria / coworking` (ID `10047369`).
- **Résultat attendu :** identifier précisément les quatre murs, prévisualiser les dépendances avec `dryRun: true`, les supprimer, puis confirmer leur absence avec un outil de lecture.
- **Résultat obtenu :** la recherche avec `useRoomSolid: true` retourne 0 mur. Avec `useRoomSolid: false`, elle trouve quatre cloisons `CLO_Cloison10` (IDs `11020580` à `11020583`). Leur géométrie confirme un carré d'environ 1,5 × 1,5 m. Le dry-run prévoit 4 suppressions et 0 dépendance ; l'exécution supprime bien les 4 murs. La recherche spatiale suivante ne retourne plus aucun de ces IDs.
- **Anomalie :** le mode solide réel exclut les murs bordant la pièce, tandis que le mode boîte englobante est sensible au changement de géométrie de la pièce : après suppression, il retourne 12 autres murs parce que la boîte de la pièce s'est agrandie. Le comportement est juste selon les algorithmes employés, mais ambigu pour un scénario « murs dans/autour d'une pièce ».
- **Amélioration proposée :** ajouter un mode explicite `intersectsRoomBoundary` ou `roomBoundingElements` pour récupérer les éléments de contour d'une pièce sans dépendre de sa boîte englobante. Retourner les limites utilisées et préciser la sémantique de `useRoomSolid` dans la réponse.

### `get_element_parameters` sur des IDs supprimés

- **Scénario :** vérifier après suppression que les quatre IDs de murs n'existent plus.
- **Résultat attendu :** signaler clairement quatre éléments introuvables, avec un compteur ou une liste `invalidIds`/`notFoundIds`.
- **Résultat obtenu :** la réponse annonce `Retrieved parameters for 4 elements`, mais chaque ligne contient `elementName: null`, `category: null` et une liste de paramètres vide.
- **Anomalie :** le message de succès est trompeur et ne distingue pas un élément supprimé d'un élément valide sans paramètres.
- **Amélioration proposée :** retourner `foundCount`, `notFoundCount` et `notFoundIds`, et exclure ou marquer explicitement les lignes absentes avec `found: false`.

## 2026-08-20 — Sauvegarde des suppressions dans la V3

### `save_document`

- **Scénario :** enregistrer dans le fichier V3 actif la suppression des quatre cloisons de la cafétéria.
- **Résultat attendu :** sauvegarder le document à son chemin courant, puis confirmer le chemin actif et la mise à jour du fichier.
- **Résultat obtenu :** succès en environ 2,2 s ; `save_document` renvoie `Saint-Malo_avenue aristide briand_46_V3.rvt`. `get_project_info` confirme le même chemin et le fichier présente un nouvel horodatage ainsi qu'une taille de 216 711 168 octets.
- **Anomalie :** l'outil n'expose pas de véritable `dryRun`, comme `save_as_document`. Son schéma accepte un objet libre, mais l'implémentation ignore les entrées ; fournir artificiellement `dryRun: true` déclencherait donc quand même la sauvegarde.
- **Amélioration proposée :** ajouter un paramètre `dryRun` réel qui retourne le chemin, l'état modifié du document, l'existence/accessibilité de la cible et les risques prévisibles sans appeler `Document.Save()`.

## 2026-08-21 — Reprise de session et enregistrement V4

### `save_as_document` — fausse anomalie signalée par erreur d'agent

- **Scénario :** enregistrer le document actif `_V3.rvt` sous `_V4.rvt`, d'abord avec `dryRun: true` puis sans, en utilisant un paramètre nommé `filePath`.
- **Résultat attendu :** aperçu ou exécution réussie, comme le 2026-08-20.
- **Résultat obtenu initialement :** échec systématique avec un message générique non structuré `An error occurred invoking 'save_as_document'`, y compris avec un chemin cible simplifié sans espace ni accent — signalé à tort comme une régression de l'outil.
- **Cause réelle :** le schéma de l'outil attend le paramètre `targetPath`, pas `filePath`. L'agent utilisait un nom de paramètre erroné (résidu de mémoire d'une session antérieure), ce qui provoquait une exception non catchée côté routeur plutôt qu'une erreur de validation claire. Avec `targetPath`, l'enregistrement réussit du premier coup.
- **Anomalie confirmée malgré tout :** un mauvais nom de paramètre ne doit pas produire une exception .NET non structurée qui ressemble à une panne de l'outil. Cela contrevient à la règle AGENTS.md « must not leak exceptions across the router » et a coûté plusieurs cycles de diagnostic inutiles (chemin, cache, état du document) avant d'identifier la vraie cause.
- **Amélioration proposée :** valider les paramètres reçus contre le schéma MCP avant d'invoquer l'implémentation, et renvoyer une erreur `InvalidInput` explicite (nom de paramètre attendu vs reçu) plutôt que de laisser fuir l'exception d'appel.

### Reconfirmation — cache de `get_project_info` après `save_as_document`

- **Scénario :** vérifier `get_project_info` avec la signature de lecture déjà utilisée juste après un `save_as_document` réussi (V3 → V4).
- **Résultat obtenu :** premier appel (mêmes paramètres implicites) renvoie encore le chemin `_V3.rvt` obsolète malgré le succès de la sauvegarde. Un second appel avec une signature différente (`includeLinks: true`, `includeWorksets: true`) renvoie immédiatement le chemin `_V4.rvt` correct.
- **Anomalie :** confirme le comportement déjà consigné le 2026-08-20 — le cache de portée `Document` n'est pas invalidé par l'événement `Save As`, uniquement contourné en changeant la signature de la requête.
- **Amélioration proposée :** inchangée — invalider le cache sur l'événement Revit de Save As, ou exclure `Document.PathName`/`Document.Title` de la mise en cache, et exposer un indicateur `cached` dans la réponse.

## 2026-08-21 — Inventaire des fenêtres et localisation FR

### `export_elements_data` — paramètres localisés non résolus en anglais

- **Scénario :** exporter `Mark`, `Type Name`, `Level`, `Width`, `Height` pour la catégorie `Windows` (145 éléments), document en français.
- **Résultat attendu :** valeurs renseignées pour les paramètres intégrés demandés.
- **Résultat obtenu :** les 5 colonnes reviennent vides pour les 145 éléments, sans avertissement. En relançant avec les noms français (`Repère`, `Niveau`, `Largeur`, `Hauteur`), `Niveau`, `Largeur` et `Hauteur` sont correctement renseignés (`Repère` reste vide car non rempli sur ces occurrences).
- **Anomalie :** l'outil résout apparemment les noms de paramètres par correspondance de chaîne sur le nom affiché localisé, sans repli sur les `BuiltInParameter` indépendants de la langue ni alias anglais/français. Le résultat vide est silencieux — aucun `unresolvedParameterNames` n'est renvoyé, contrairement à la promesse générale AGENTS.md d'« utiliser des IDs indépendants de la langue quand c'est possible ».
- **Amélioration proposée :** mapper les noms courants (`Mark`/`Repère`, `Level`/`Niveau`, `Width`/`Largeur`, `Height`/`Hauteur`, `Type Name`/`Type`) vers leurs `BuiltInParameter` respectifs indépendamment de la langue du document, ou au minimum renvoyer une liste `unmatchedParameterNames` dans la réponse pour signaler l'échec de résolution au lieu de colonnes vides.

### `export_elements_data` — pas de mode comptage/aperçu avant export volumineux

- **Scénario :** même export avec `includeTypeParameters: true` sur 145 éléments.
- **Résultat obtenu :** réponse de 405 914 caractères dépassant la limite de sortie côté client MCP, sans avoir pu dimensionner l'appel au préalable.
- **Amélioration proposée :** ajouter un mode `countOnly` ou une estimation de taille de réponse avant l'export complet.

### `create_schedule` / `modify_schedule` — même problème de localisation, message d'erreur trompeur

- **Scénario :** créer une nomenclature « Fenêtres » avec les champs `Mark`, `Type Name`, `Level`, `Width`, `Height`, `Count` (noms anglais).
- **Résultat obtenu :** seuls `Height` et `Count` sont acceptés. `Mark`, `Type Name`, `Level`, `Width` sont rejetés avec la raison `NotSchedulableForCategory`, alors qu'ils existent bien pour la catégorie sous leurs noms français (`Repère`, `Famille et type`/`Type`, `Niveau`, `Largeur`), confirmés présents dans `schedulableFieldNames` retourné par le même appel. En relançant `modify_schedule` (`add_field`) avec les noms français, 3 des 4 champs sont ajoutés avec succès et remplis correctement (`Famille et type`, `Niveau`, `Largeur`) ; `Repère` seul est refusé, cause à approfondir.
- **Anomalie :** la raison `NotSchedulableForCategory` est fausse dans ce cas précis — le champ existe et est planifiable, seul le nom fourni est dans la mauvaise langue. Le message induit en erreur sur la cause réelle (nom vs compatibilité de catégorie).
- **Amélioration proposée :** distinguer explicitement dans la réponse "paramètre introuvable sous ce nom" (avec suggestions déjà présentes dans `skippedFields[].suggestions`, bien utiles) de "paramètre non planifiable pour cette catégorie" (vraie limitation Revit). Le champ `suggestions` existant est une bonne base ; il faudrait qu'il inclue aussi les variantes localisées correspondantes.

### `get_schedule_data` — `availableFields` non filtrable, gonfle la réponse malgré `maxRows`

- **Scénario :** lire 10 lignes (`maxRows: 10`) d'une nomenclature de 147 fenêtres.
- **Résultat obtenu :** `maxRows` est bien respecté pour `rows` (10 lignes retournées), mais la réponse dépasse quand même la limite de sortie (137 145 caractères) à cause du tableau `availableFields`, qui liste plusieurs centaines de paramètres schedulables du projet (paramètres partagés, fabricants, IFC, etc.) systématiquement inclus en intégralité.
- **Amélioration proposée :** rendre `availableFields` optionnel (`includeAvailableFields: false` par défaut) ou le paginer/filtrer, puisqu'il est indépendant du nombre de lignes demandées et non nécessaire à chaque appel.

### Nouvel essai confirmé — nomenclature fenêtres avec noms de champs français

- **Scénario :** reprendre le test avec `create_schedule` en utilisant uniquement les noms français (`Repère`, `Famille et type`, `Niveau`, `Largeur`, `Hauteur`, `Total`) pour la catégorie `Windows`.
- **Résultat obtenu :** succès pour 5 des 6 champs (`Famille et type`, `Niveau`, `Largeur`, `Hauteur`, `Total`), confirmé par `get_schedule_data` avec des valeurs correctement renseignées sur les 147 fenêtres (ex. `FEN_2PF: 120x215ht 16 | R+1 | 1.20 | 2.15 | 1`). Seul `Repère` est rejeté, avec `NotSchedulableForCategory` et une suggestion `ARC_PAR_Repère` — cette fois la raison semble correcte : `Repère` (le paramètre `Mark` intégré) n'apparaît pas du tout dans `schedulableFieldNames` pour `Windows` dans ce projet, seul le paramètre projet personnalisé `ARC_PAR_Repère` existe. À confirmer si c'est une particularité de ce modèle (paramètre `Mark` non exposé/masqué pour cette catégorie) ou une limitation de l'outil.
- **Conclusion :** le contournement (fournir les noms de champs dans la langue du document) fonctionne de façon fiable pour `create_schedule`/`modify_schedule` comme pour `export_elements_data`. Nomenclature de test supprimée après vérification (`delete_schedule`), aucune trace laissée dans le modèle.

### Proposition de correction pour le problème de langue (transverse à plusieurs outils)

Le problème touche au minimum `export_elements_data`, `create_schedule` et `modify_schedule`, et probablement d'autres outils acceptant des noms de paramètres en entrée libre. Ces outils comparent le nom fourni au nom *affiché* du paramètre dans la langue active du document Revit (ici le français), sans normalisation. Un agent ou un utilisateur non francophone, ou simplement habitué aux noms API anglais (`Mark`, `Level`, `Width`, `Height`, `Type Name`, `Comments`...), obtient un échec silencieux (colonne vide) ou un message d'erreur trompeur (`NotSchedulableForCategory` alors que c'est un problème de nom).

Proposition en trois niveaux, du plus simple au plus complet :

1. **Table d'alias pour les `BuiltInParameter` courants.** Maintenir un dictionnaire statique anglais → `BuiltInParameter` (ex. `Mark → BuiltInParameter.ALL_MODEL_MARK`, `Level → BuiltInParameter.FAMILY_LEVEL_PARAM`/paramètre de niveau selon catégorie, `Width → BuiltInParameter.GENERIC_WIDTH` ou équivalent catégorie, `Height`, `Comments`, `Type Name`, `Family`, `Family and Type`, `Count`, `Type Mark`...). Résoudre d'abord par alias anglais connu, puis retomber sur la correspondance par nom affiché localisé si l'alias ne matche pas. Couvre le cas le plus fréquent (paramètres intégrés) sans dépendre de la table de langue de Revit.
2. **Résolution insensible à la casse/accents + suggestion automatique.** Pour les paramètres non couverts par la table d'alias (paramètres partagés/projet, comme `ARC_PAR_Repère`), comparer en ignorant la casse et les accents, et en cas d'échec renvoyer une liste de suggestions par proximité (Levenshtein) sur `schedulableFieldNames`/paramètres disponibles de l'élément — le mécanisme `suggestions` déjà présent dans `create_schedule` est une bonne base, à généraliser à `export_elements_data`, `filter_by_parameter_value`, `bulk_modify_parameter_values`, etc.
3. **Feedback explicite au lieu d'un échec silencieux.** Quel que soit le mécanisme de résolution, ne jamais renvoyer une colonne vide sans avertissement : ajouter systématiquement un champ `unresolvedParameterNames` (avec suggestions) dans la réponse de tout outil qui accepte des noms de paramètres en entrée libre, pour que l'agent/l'utilisateur sache immédiatement qu'un nom n'a pas été reconnu plutôt que d'interpréter des valeurs vides comme des données réelles.

Bénéfice attendu : les agents IA (souvent entraînés sur des noms de paramètres Revit en anglais, API-first) et les utilisateurs de projets non francophones cesseront de rencontrer des échecs silencieux ou des diagnostics erronés (« NotSchedulableForCategory ») quand le seul problème est la langue d'affichage du document.

## 2026-08-21 — Mur de refend et découpe de la pièce Cafétéria en deux

### `get_available_family_types` — `compact: true` renvoie systématiquement une liste vide

- **Scénario :** lister les types de murs disponibles (`categoryList: ["Walls"]`, puis `["Murs"]`, puis `["OST_Walls"]`) pour trouver un type de cloison à réutiliser.
- **Résultat attendu :** liste des types système de murs du projet.
- **Résultat obtenu :** `count: 0` dans tous les cas avec `compact: true`, y compris sans aucun filtre de catégorie (`{"compact": true, "limit": 10}` sur l'ensemble du projet). Le même appel sans `compact` (ou `compact: false`) retourne des résultats corrects.
- **Anomalie :** le paramètre `compact` semble casser entièrement la sérialisation de la réponse de cet outil plutôt que simplement l'alléger. Confirmé indépendamment du filtre de catégorie.
- **Amélioration proposée :** corriger le chemin `compact: true` de `get_available_family_types` ; ajouter un test de non-régression comparant le nombre d'éléments retournés avec/sans `compact` pour le même appel.

### `get_available_family_types` — `categoryList` ne filtre jamais (anglais, français, ou `OST_`)

- **Scénario :** filtrer par catégorie avec `categoryList: ["Walls"]`, `["Murs"]`, `["OST_Walls"]`.
- **Résultat obtenu :** `count: 0` dans les trois cas, alors qu'un appel sans aucun filtre montre bien des types de la catégorie « Murs » présents dans le projet (ex. `CLO_Cloison10`, id 10108183). Seul `familyNameFilter` (recherche texte sur le nom) fonctionne pour cibler les murs.
- **Anomalie :** la description du paramètre annonce « Filter by category names (OST codes, English, or localized labels) », mais aucune des trois formes ne fonctionne en pratique pour ce test.
- **Amélioration proposée :** vérifier la résolution de `categoryList` vers `BuiltInCategory` (probable confusion entre nom de catégorie système et nom affiché localisé, écho du problème de langue documenté plus haut) ; couvrir les trois formats annoncés dans la documentation par des tests.

### `create_wall` réussi, mais unités incohérentes dans `get_element_parameters`

- **Scénario :** créer un mur de refend (`CLO_Cloison10`, type id 10108183) au centre de la pièce « Cafétéria / coworking » (RDC) pour la couper en deux, `dryRun: true` puis exécution réelle.
- **Résultat obtenu :** succès complet (aperçu cohérent, création confirmée, `Limite de pièce: 1`). Mais l'appel de vérification `get_element_parameters` sur le mur créé renvoie `Longueur`, `Hauteur sans contrainte`, `Volume`, `Surface` en unités internes Revit (pieds, pieds carrés, pieds cubes) sans étiquette d'unité, alors que `create_wall`, `get_element_solid_geometry` et la plupart des autres outils travaillent en mm/m/m². Un agent qui ne connaît pas cette exception lira une « Surface: 122.81 » en pensant m², à tort (122,81 pieds² ≈ 11,41 m²).
- **Amélioration proposée :** convertir systématiquement les paramètres numériques de longueur/aire/volume en unités du projet (ou en mm/m²/m³ comme le reste de l'API) dans `get_element_parameters`, ou a minima ajouter une unité explicite à chaque valeur retournée (`{"value": 122.81, "unit": "ft²"}`).

### `create_line_based_element` — catégorie « ligne de séparation de pièce » non supportée

- **Scénario :** tenter de couper la pièce avec une véritable ligne de séparation de pièces (Room Separation Line) plutôt qu'avec le mur, via `create_line_based_element` avec `category: "RoomSeparationLines"` puis `category: "OST_RoomSeparationLines"`.
- **Résultat obtenu :** `RoomSeparationLines` → catégorie non reconnue. `OST_RoomSeparationLines` → catégorie reconnue mais rejetée avec `"No family types available for category OST_RoomSeparationLines"`. Les lignes de séparation de pièces sont une catégorie sans type de famille (comme les lignes de détail), ce que l'implémentation actuelle de `create_line_based_element` ne gère pas (elle suppose un `FamilySymbol`/type pour toute catégorie).
- **Anomalie :** aucun outil RiveTT exposé ne permet de créer une ligne de séparation de pièces. Le contournement utilisé (mur porteur de limite de pièce, `Limite de pièce: 1`) fonctionne pour ce scénario mais n'est pas équivalent (un mur physique n'est pas une simple ligne de séparation, et n'est pas toujours souhaitable architecturalement).
- **Amélioration proposée :** ajouter un chemin dédié dans `create_line_based_element` (ou un nouvel outil `create_room_separation_line`) pour les catégories de courbes de modèle sans type de famille (lignes de séparation de pièces, lignes de pièce, lignes de surface).

### `create_room` sans `dryRun`, piège de placement dans une zone déjà occupée

- **Scénario :** après la pose du mur de refend, placer un second point de pièce de l'autre côté pour représenter les deux moitiés de la cafétéria.
- **Résultat obtenu (1er essai) :** le point placé (x=-11500) est tombé du même côté du mur que la pièce d'origine (celle-ci occupe en réalité le côté X -13660 à -9380, pas le côté attendu). La nouvelle pièce est créée sans erreur mais reste non délimitée (`Surface`, `Périmètre`, `Volume` tous `null`/`hasValue: false`), sans avertissement retourné par l'outil lui-même — seul un examen approfondi de `get_element_parameters` (`includeTypeParameters:false`, vue complète) l'a révélé. Corrigé en replaçant le point du bon côté (x=-16000) : pièce correctement délimitée, aire cohérente avec la moitié attendue.
- **Anomalie :** `create_room` n'expose pas de paramètre `dryRun` (contrairement à la règle générale du projet de prévisualiser toute écriture), et surtout ne signale pas dans sa réponse immédiate que la pièce créée est non délimitée/en conflit — il faut interroger séparément les paramètres pour s'en apercevoir. Une pièce non délimitée dans une zone occupée est un piège classique en modélisation Revit ; sans warning explicite, l'agent (ou l'utilisateur) peut croire l'opération réussie alors qu'elle ne produit aucune pièce exploitable.
- **Amélioration proposée :** ajouter `dryRun` à `create_room` ; faire retourner par l'outil un indicateur explicite `enclosed: false` / `area: null` avec un message d'avertissement dès la création (pas seulement visible via `get_warnings` ou une relecture manuelle des paramètres), en s'appuyant sur les avertissements Revit natifs (« Les pièces en surbrillance se chevauchent » observé dans `get_warnings`).

### `delete_element` — décompte supérieur à la liste d'éléments supprimés

- **Scénario :** supprimer la pièce mal placée (1 ID fourni).
- **Résultat obtenu :** `"message": "Deleted 2 element(s) successfully."` et `"deletedCount": 2`, mais `deletedElements` ne liste qu'un seul élément (la pièce). L'élément supplémentaire supprimé (vraisemblablement l'étiquette de pièce associée) n'est pas identifié.
- **Amélioration proposée :** lister systématiquement tous les éléments réellement supprimés (y compris les dépendances implicites comme les tags), pour que `deletedElements.length` corresponde toujours à `deletedCount`.

## 2026-08-21 — Série de tests libres (porte, fenêtre, copie, similaire, lignes, garde-corps, type de mur, escalier)

Contexte : campagne libre sur modèle de test, `dryRun` allégé pour les écritures simples/unitaires réversibles (conservé pour suppressions et opérations multi-éléments), conformément à l'échange précédent avec l'utilisateur.

### 1. `create_door` — porte hébergée dans un mur (mur de refend RDC)

- Succès direct, dryRun puis exécution cohérents (`hostWallId: 11020637`, `typeId: 10019929` « PTE_Porte simple / PP 93x204 »). Aucune anomalie.

### 2. `create_window` — fenêtre dans un autre mur (mur extérieur existant)

- Succès direct sur le mur extérieur `10177830` (déjà porteur d'une autre fenêtre du même type), à une position différente. Aucune anomalie ; confirme qu'un mur peut recevoir plusieurs fenêtres du même type sans conflit.

### 3. `copy_elements` — copie d'un mur (avec sa fenêtre) d'un étage à l'autre, puis porte dans le mur copié

- **Résultat obtenu :** `copy_elements` avec seul `offsetZ` (3400mm, RDC→R+1) a correctement réassocié les contraintes `Contrainte inférieure`/`Contrainte supérieure` du mur copié aux niveaux réels (R+1→R+2) plutôt que de conserver un décalage brut en Z. La fenêtre copiée avec le mur est restée hébergée correctement. Une porte a ensuite été ajoutée sans problème dans le mur copié.
- **Constat positif, pas d'anomalie :** ce comportement (recalcul automatique des contraintes de niveau après copie verticale) est le comportement Revit natif attendu et fonctionne correctement via ce connecteur.

### 4. Fonction « créer similaire » — dupliquer une fenêtre déjà présente

- **Constat :** RiveTT n'expose pas de fonction nommée « créer similaire ». `copy_elements` (copie avec décalage, même document) en tient lieu efficacement : la fenêtre copiée s'est ré-hébergée automatiquement sur le mur présent à sa nouvelle position, sans qu'il soit nécessaire de repréciser un hôte. Pas d'anomalie, mais cette équivalence n'est pas documentée en tant que telle — à mentionner dans la doc utilisateur/agent pour éviter qu'un agent cherche en vain un outil dédié.

### 5. Lignes 2D (détail) et 3D (modèle) — non supporté

- **Scénario :** créer une ligne de détail (`category: "Lines"`) puis une ligne de modèle (`category: "OST_Lines"`) via `create_line_based_element`.
- **Résultat obtenu :** `"Lines"` → catégorie non reconnue. `"OST_Lines"` → catégorie reconnue mais rejetée avec `"No family types available for category OST_Lines"`, exactement comme pour les lignes de séparation de pièces (2026-08-21, test précédent). `create_line_based_element` est conçu uniquement pour des éléments de ligne basés sur une famille (murs, poutres) et ne gère aucune catégorie de courbe pure (lignes de détail, lignes de modèle, lignes de séparation de pièces).
- **Anomalie :** aucun outil RiveTT ne permet de tracer une ligne 2D ou 3D générique. C'est une lacune fonctionnelle transverse (troisième occurrence du même type de blocage après les lignes de séparation de pièces).
- **Amélioration proposée :** ajouter un outil dédié `create_detail_line`/`create_model_line` (ou étendre `create_line_based_element` avec un mode « courbe pure » sans `FamilySymbol`) pour couvrir les catégories `OST_Lines` (lignes de modèle) et les lignes de détail (portées par une vue).

### 6. `create_railing` — garde-corps

- Succès. Type existant du projet retrouvé via `export_elements_data` sur la catégorie française « Garde-corps » (le nom anglais `Railings` et le préfixe `OST_Railings` sont rejetés par `export_elements_data`, cohérent avec le problème de langue déjà documenté). Garde-corps tracé sur un chemin de 2 points, dryRun puis exécution cohérents.
- **Anomalie mineure associée :** `get_available_family_types` ne retourne aucun résultat pour les types système de garde-corps quel que soit le terme cherché (« Garde », « rail », « Handrail », « 1100 ») car ce sont des types système, pas des familles chargeables — cohérent avec le comportement déjà observé pour les murs (seul `duplicate_system_type`/`export_elements_data` permettent de retrouver un type système existant).

### 7. Nouveau type de mur 480mm multicouche

- **Étape 1 :** `duplicate_family_type` échoue sur un type de mur (`"Element ... not found or is not a FamilySymbol"`) car les murs sont des types système, pas des familles chargeables. `duplicate_system_type` (action `duplicate`) est le bon outil et fonctionne correctement.
- **Étape 2 :** `set_compound_structure` avec des noms de matériaux inventés (ex. « Enduit plâtre », « Béton banché ») est accepté silencieusement : les couches sont créées avec les bonnes épaisseurs mais `materialName: "(none)"`, sans avertissement ni erreur. Après correction avec des noms de matériaux réels du projet (`ARC_MAT_PLATRE`, `ARC_MAT_ISOLATION`, `ARC_MAT_BETON`, `ARC_MAT_ISOLATION RIGIDE`, `ARC_MAT_ENDUIT GRIS`), tout fonctionne : 5 couches, 480mm au total, couche structurelle correctement identifiée (`isStructural: true` sur la couche béton), vérifié via `get_compound_structure`.
- **Anomalie associée :** `get_materials` avec `nameFilter: "béton"` retourne les 221 matériaux du projet sans filtrage (paramètre ignoré), obligeant à parcourir la liste complète pour trouver un nom exploitable — reproduction du même défaut de filtre déjà noté sur `get_available_family_types.categoryList` et `export_elements_data`.
- **Amélioration proposée :** faire échouer explicitement `set_compound_structure` (ou renvoyer un avertissement `materialNotFound`) quand `materialName` ne correspond à aucun matériau du projet, plutôt que de créer une couche sans matériau assigné. Corriger `get_materials.nameFilter` pour qu'il filtre réellement.

### 8. Création d'un escalier — aucun outil disponible

- **Constat :** recherche exhaustive de tout outil RiveTT lié aux escaliers (`create_stair`, `stairs`, `escalier`, `run`, `landing`, `flight`) : aucun résultat. Il n'existe aucun moyen de créer un escalier natif Revit via ce connecteur.
- **Anomalie :** lacune fonctionnelle majeure pour un usage architecture réel (bâtiment R+6 avec circulations verticales). Cohérent avec les limitations déjà déclarées par `get_server_capabilities.lifecycleLimitations` (`open_document`, `edit_family`) : la création d'escalier standard Revit passe par un éditeur d'esquisse modal (Stair by Sketch) difficilement pilotable depuis un `ExternalEvent` non modal, ce qui explique probablement l'absence de l'outil plutôt qu'un simple oubli.
- **Amélioration proposée :** documenter explicitement cette limitation dans `get_server_capabilities.lifecycleLimitations` (comme pour `open_document`) pour que les agents ne perdent pas de temps à chercher l'outil ; évaluer si l'API Revit 2027 permet de construire un escalier par composant (`StairsEditScope` non-interactif ou création par `Stairs.CreateSketchedStairs`) sans passer par l'UI modale, pour une future implémentation.

## 2026-08-21 — Niveau, coupe, feuille de présentation, export PDF

### `create_level` (create + set) et `create_view` (Section avec gabarit et profondeur)

- Succès complet et sans anomalie : création d'un niveau (`TEST_Niveau MCP`, 20000mm), modification de son altimétrie (`action: set`, 20500mm), création d'une coupe avec gabarit appliqué dès `create_view` (`templateId`), puis réglage de la profondeur via `set_element_parameters` (`VIEWER_BOUND_ACTIVE_FAR` + `VIEWER_BOUND_OFFSET_FAR`). Tout confirmé par relecture.

### Éléments introuvables après copie inter-étages — fausse alerte, cause réelle : navigation

- **Scénario :** l'utilisateur ne retrouvait pas le mur/fenêtre/porte copiés au R+1 (test du 2026-08-21 précédent).
- **Vérification :** les trois éléments existent bien, correctement rattachés au niveau R+1 (id 512913), avec géométrie réelle non nulle. La vue active de l'utilisateur était "R+1 Copie 1" (un plan dupliqué distinct du plan R+1 standard) et les éléments sont situés à l'angle du bâtiment où se trouvait le mur source (loin de la zone cafétéria travaillée juste avant) — probablement simplement hors du cadrage/zoom courant de l'utilisateur, pas un problème de l'outil.
- **Amélioration proposée :** aucune côté MCP ; suggérer aux utilisateurs de vérifier la vue active et de faire un "Zoom to Fit"/sélection par ID après une copie inter-niveaux, pour éviter cette confusion récurrente.

### `create_sheet.titleBlockId` — paramètre totalement non fonctionnel (bug bloquant, reproduit 2/2)

- **Scénario :** créer une feuille de présentation avec un cartouche existant, d'abord `CAR_club_Grand format` (id 9591271), puis retest avec le cartouche standard du projet `FEUILLE/A3H` (id 8966185, utilisé sur la majorité des feuilles existantes).
- **Résultat obtenu :** dans les deux cas, `create_sheet` réussit mais la feuille créée a pour `Famille et type` la valeur `593` — le type système « Feuille » par défaut de Revit, sans aucun cartouche, avec une taille fixe de 210×297mm (A4). Le paramètre `titleBlockId` fourni est totalement ignoré, sans erreur ni avertissement.
- **Impact concret :** la feuille produite est visuellement vide (pas de cartouche, pas de cadre, pas de bloc titre) et de taille minuscule par rapport à une feuille de présentation réelle (A1/A0 attendue), ce qui explique aussi que la vue placée dessus paraisse « excentrée » : elle est en fait correctement centrée sur une feuille beaucoup plus petite que prévu.
- **Tentative de contournement — échec :** `create_point_based_element` avec `category: "Cartouches"` et le bon `typeId` ne permet pas de cibler une feuille précise (le schéma n'expose qu'un `levelId`, pas de `sheetId`/`viewId`) ; le dryRun montre que l'instance serait rattachée à un niveau de plan (RDC par défaut) et non posée sur la feuille — ce qui produirait un cartouche orphelin ailleurs dans le modèle plutôt qu'un vrai cartouche de feuille. Non exécuté pour éviter de polluer le modèle.
- **Anomalie confirmée bloquante :** aucun outil ni combinaison d'outils RiveTT ne permet actuellement de produire une feuille de présentation avec cartouche fonctionnel.
- **Amélioration proposée :** corriger `create_sheet` pour qu'il applique réellement `titleBlockId` (probable oubli de passer le type au constructeur `ViewSheet.Create(document, titleBlockTypeId)` au lieu de `ViewSheet.Create(document, ElementId.InvalidElementId)` suivi d'une étape de placement manquante). À défaut, exposer un outil dédié `place_title_block(sheetId, titleBlockTypeId)` s'appuyant sur `NewFamilyInstance(location, symbol, sheetView)`.

### `delete_element` sur une feuille (`ViewSheet`) — comportement incohérent

- **Scénario :** supprimer une feuille de test défectueuse (sans cartouche), avec puis sans viewport placé dessus.
- **Résultat obtenu :** la suppression d'une feuille strictement vide (aucun viewport placé) réussit (`Deleted 6 element(s)`, incluant des sous-éléments implicites non détaillés). La suppression d'une feuille strictement identique mais portant un viewport échoue systématiquement (`"One or more of the elementIds cannot be deleted. Parameter name: elementIds"`), y compris après suppression préalable du viewport lui-même — la feuille reste bloquée durablement même une fois vidée. Cause exacte non identifiée (peut-être un état de régénération Revit non rafraîchi après suppression du viewport, ou une référence résiduelle).
- **Anomalie :** comportement non déterministe/reproductible de façon inexpliquée ; le message d'erreur est une exception .NET brute non traduite en diagnostic exploitable (cf. anomalie similaire déjà notée sur `save_as_document`).
- **Amélioration proposée :** capturer et traduire l'exception `ArgumentException` de `Document.Delete` en message explicite (ex. "la feuille est référencée par X, Y" ou "régénération requise avant suppression"), et vérifier si une régénération du document (`Document.Regenerate()`) après suppression du viewport résout le blocage.

### `delete_element` — mauvais libellé de catégorie pour les viewports

- **Scénario :** supprimer un viewport (vue placée sur feuille), puis l'interroger via `get_element_solid_geometry`.
- **Résultat obtenu :** dans les deux cas, la catégorie retournée est `"Fenêtres "` (avec espace parasite en fin de chaîne) au lieu de la catégorie réelle (Viewports / « Vues portées »). Reproduit deux fois (suppression + lecture géométrie) sur deux viewports différents.
- **Anomalie :** mapping de catégorie erroné pour `OST_Viewports`, probablement une résolution de nom de catégorie qui retombe par défaut sur une autre catégorie proche dans l'énumération (Windows/Fenêtres), plus l'espace résiduel suggérant un problème de troncature de chaîne.
- **Amélioration proposée :** corriger la résolution du nom de catégorie pour `OST_Viewports` dans les outils génériques (`delete_element`, `get_element_solid_geometry`, et probablement `get_element_parameters`).

## 2026-08-21 — Création d'un appartement T2 neuf (fichier dédié, travail autonome)

Contexte : demande utilisateur de créer un nouveau fichier avec le gabarit architectural, d'y modéliser un T2 d'une quarantaine de m² (balcon, WC séparé, placard, dalle haute/basse, dalle sur plot, garde-corps) conforme à un standard promoteur/bailleur social, de le sauvegarder sur le Bureau, de produire une mise en page et un export PDF. Travail mené en autonomie avec contournements documentés à chaque blocage.

### Absence de tout outil de création de document — `save_as_document` utilisé en substitut

- **Scénario :** créer un nouveau fichier Revit vierge basé sur le gabarit architectural, indépendant du modèle Saint-Malo en cours.
- **Résultat attendu :** un outil type `new_document`/`create_project(templatePath)` permettant de partir d'un `.rte` propre.
- **Résultat obtenu :** recherche exhaustive des ~280 outils RiveTT (via `ToolSearch` et `get_server_capabilities.lifecycleLimitations`) : aucun outil de création de document n'existe. Seul `save_as_document` est disponible, et il **duplique le document actuellement ouvert** (avec tous ses niveaux, familles, éléments de test antérieurs) sous un nouveau chemin — ce n'est pas un nouveau projet vierge.
- **Anomalie :** blocage fonctionnel majeur, cohérent avec la limitation déjà documentée sur `open_document` (ExternalEvent ne peut piloter `Application.NewProjectDocument`/`OpenAndActivateDocument`). Conséquence concrète : le fichier livré `Appartement_T2_MCP_Test.rvt` contient encore la totalité de l'opération Saint-Malo (tous les niveaux, familles, la Nomenclature Fenêtres de test, etc.), simplement enrichi de deux nouveaux niveaux et de l'appartement T2. Ce n'est pas le "gabarit architectural" demandé.
- **Contournement appliqué :** `save_as_document` depuis le document Saint-Malo déjà ouvert (qui est un projet basé sur un gabarit architectural francophone), en ajoutant deux niveaux dédiés très au-dessus du bâtiment existant (z=30,00 m et 32,50 m) pour isoler géométriquement le T2 et éviter toute interférence visuelle avec le modèle existant.
- **Amélioration proposée :** exposer un outil `create_document(templatePath, targetPath)` s'appuyant sur `Application.NewProjectDocument` hors du dispatcher `ExternalEvent` (orchestrateur dédié, cf. limitation déjà remontée pour `open_document`/`edit_family`). À défaut, documenter clairement dans `get_server_capabilities` que "nouveau document" doit se lire "dupliquer le document actif".

### `get_element_parameters` et `get_element_solid_geometry` — régression totale sur le nouveau document

- **Scénario :** vérifier les surfaces des pièces créées (Séjour/Cuisine, Chambre, WC, Salle de bains) et la géométrie des murs via ces deux outils de lecture, jusqu'ici fiables durant la campagne.
- **Résultat attendu :** obtenir surfaces/paramètres et solides comme dans le fichier Saint-Malo précédemment testé.
- **Résultat obtenu :** échec systématique — `An error occurred invoking 'get_element_parameters'` et `An error occurred invoking 'get_element_solid_geometry'` sur **tout** elementId testé (pièces, mur, avec ou sans `parameterNames`), y compris sur des éléments venant d'être créés avec succès. `get_project_info` et `create_*` continuent de fonctionner normalement sur le même document.
- **Anomalie :** régression reproductible et bloquante, isolée à ces deux outils de lecture d'éléments, apparue après le `save_as_document`. Cause probable : cache d'éléments ou résolution de document non rafraîchie après un changement de document actif via `save_as_document` (à rapprocher du bug de cache déjà documenté sur `get_project_info`, ici avec un impact plus sévère car sans contournement paramétrique possible).
- **Contournement appliqué :** aucun disponible ; vérification des pièces et murs basée uniquement sur les réponses de succès des outils de création (`create_room`, `create_wall`, `create_floor`), sans double contrôle géométrique indépendant — donc sans garantie totale de non-chevauchement ou d'enclosure correcte des pièces.
- **Amélioration proposée :** invalider/rafraîchir tout cache d'éléments interne dès qu'un `save_as_document` (ou tout changement de document actif) est exécuté. Ajouter un test de non-régression couvrant "lecture d'élément juste après save_as_document".

### `export_elements_data` — le filtre `elementIds` est non fonctionnel

- **Scénario :** cibler un élément précis (le garde-corps de test 11020716) via `export_elements_data({elementIds:[11020716], parameterNames:["Famille et type"]})`.
- **Résultat attendu :** recevoir uniquement les données de cet élément.
- **Résultat obtenu :** le filtre est ignoré ; la réponse contient les 100 premiers éléments du modèle entier (`categoriesUsed: ["All"]`, `filteredCount: 9282`), identique à un appel sans filtre.
- **Anomalie :** s'ajoute aux filtres déjà documentés comme non fonctionnels (`categoryList`, `nameFilter`) : `elementIds` — pourtant le filtre le plus élémentaire et le plus fiable attendu — ne restreint rien.
- **Amélioration proposée :** traiter ce filtre en priorité (impact fort) : `elementIds` doit être appliqué avant toute pagination/troncature à 100 éléments, avec un test unitaire dédié.

### Impossible d'énumérer ou de créer un garde-corps (type système "Garde-corps")

- **Scénario :** poser un garde-corps réglementaire sur le balcon du T2 (NF P01-012 : hauteur de protection ≥ 1,00 m).
- **Résultat attendu :** lister les types de garde-corps disponibles dans le projet (type système, comme pour les murs/sols), puis appeler `create_railing(railingTypeId, baseLevelId, path)`.
- **Résultat obtenu :** `get_available_family_types` ne référence que des familles chargeables (étiquettes de garde-corps), pas le type système lui-même — comportement déjà documenté pour les murs/sols/feuilles. `duplicate_system_type(action=duplicate, sourceTypeName=..., category="Garde-corps")` a été tenté avec plusieurs noms plausibles (nom par défaut de gabarit français) : `ElementNotFound` à chaque fois, avec un message suggérant à tort `get_available_family_types` comme solution. Tentative de retrouver le type via un garde-corps existant du modèle Saint-Malo (`get_element_parameters`/`get_element_solid_geometry` sur l'id 11020716) : bloquée par la régression décrite ci-dessus. `create_railing(railingTypeId:1, ...)` en dry-run pour faire échouer l'appel et obtenir une liste d'IDs valides dans le message d'erreur : le message ne liste aucune alternative.
- **Anomalie :** absence totale de mécanisme pour découvrir un type système de garde-corps sans en connaître déjà le nom exact ; combinée à la régression sur les outils de lecture, ce blocage est devenu insurmontable dans la session. **Résultat : le balcon du T2 livré n'a pas de garde-corps modélisé.**
- **Contournement appliqué :** aucun contournement satisfaisant trouvé sans risquer de modéliser un faux "garde-corps" via un muret (mur bas) qui aurait été trompeur dans les nomenclatures/exports. Choix assumé de ne pas le poser plutôt que de le maquiller.
- **Amélioration proposée :** ajouter un outil `list_system_types(category)` couvrant murs, sols, plafonds, **garde-corps**, feuilles, etc. (généralisation de ce qui existe déjà en lecture seule pour les murs empilés). Corriger le message d'erreur de `duplicate_system_type` pour ne plus renvoyer une suggestion qui ne fonctionne pas pour les catégories système.

### `create_door` / `create_window` — échec silencieux si `locationPoint.z` n'est pas dans la plage du niveau

- **Scénario :** poser les portes et fenêtres du T2 avec `locationPoint.z = 0` (mètre "logique" 0, par analogie avec `create_wall.locationLine` qui accepte `z:0` en complément de `baseLevelId`).
- **Résultat attendu :** insertion réussie, le niveau (`levelId`) faisant foi pour l'altimétrie, comme pour les murs.
- **Résultat obtenu :** échec systématique (9/9) avec un message Revit non traduit en échec de transaction ("Des occurrences de ... ne coupent rien" / "Impossible de couper l'occurrence de ... du mur"), sans aucune indication que la cause est l'altimétrie. La correction (passer `z` à l'élévation absolue du niveau, ex. 30000 mm) a résolu 8/9 cas immédiatement.
- **Anomalie :** incohérence d'API entre `create_wall` (où `locationLine.z` semble relatif/indifférent car `baseLevelId` prime) et `create_door`/`create_window` (où `locationPoint.z` doit être une cote absolue de projet). Cette incohérence a coûté 9 appels en échec avant compréhension du vrai problème, le message d'erreur ne mentionnant jamais l'altimétrie.
- **Amélioration proposée :** harmoniser la sémantique de `z` sur tous les outils de création (toujours relatif au niveau fourni, ou toujours absolu — au choix, mais uniforme), et documenter le choix dans chaque description d'outil. À défaut, faire remonter un message d'erreur explicite du type "le point d'insertion est hors de la plage verticale de l'hôte" plutôt que la traduction brute de l'exception Revit.

### `create_door` — échec de insertion d'une porte de placard 1 vantail sur cloison de 1,00 m

- **Scénario :** poser une porte de placard `PTE_Placard_Battante1v` (1v) centrée sur une cloison de doublage de 1,00 m de large fermant la niche du placard de la chambre.
- **Résultat attendu :** insertion réussie, la largeur nominale du vantail étant a priori compatible avec 1,00 m de cloison.
- **Résultat obtenu :** échec ("Impossible de couper l'occurrence de 1v du mur"). Remplacement par `PTE_Placard pliante` (porte pliante) sur le même hôte, au même point d'insertion : succès immédiat.
- **Anomalie :** cause précise non identifiée (largeur réelle du type 1v supérieure à 1,00 m ? contrainte de dégagement aux extrémités de mur ?) ; aucun message ne donne la largeur requise ni la marge manquante.
- **Amélioration proposée :** faire remonter, en cas d'échec de découpe, la largeur de l'ouverture requise par le type de famille comparée à la longueur disponible sur l'hôte.

### Synthèse des choix de conception retenus (pour information, non un bug)

- Hauteur sous plafond : 2,50 m (murs contraints entre les deux niveaux dédiés `T2 - Dalle basse` et `T2 - Dalle haute`), conforme aux exigences d'habitabilité courantes en France (hauteur minimale réglementaire 2,20 m ; 2,50 m est un standard courant promoteur/bailleur).
- Mur extérieur : `MUR_Béton20 / iso14+placo2_36` (banche béton 20 cm + isolant 14 cm + placo 2×1,25 cm ≈ 36 cm), cohérent avec un objectif RE2020/RT.
- Cloisons de distribution : `ARC_Cloison distribution_10 cm`.
- WC séparé de la salle de bains (2,0 m² / 1,5 m²), conforme à la doctrine "WC indépendant" très répandue chez les bailleurs sociaux français.
- Chambre 3,5 × 3,5 m (12,25 m², hors placard), au-delà du minimum réglementaire (9 m² handicap/PMR, ~7 m² RT) pour respecter un usage confortable de type promoteur.
- Balcon 4,0 × 1,5 m (6 m²) sur dalle sur plot (type `ARC_Sol(Att.)_Béton(20)_Isolant(12)_Etanchéité(1)_Plot(-)_Dalle(4)`), garde-corps non modélisé (cf. blocage ci-dessus).
- Placard intégré dans l'angle de la chambre (1,0 × 0,6 m), porte pliante.
- Feuille de mise en page produite par duplication d'une feuille existante porteuse d'un cartouche réel (contournement du bug déjà documenté sur `create_sheet.titleBlockId`), export PDF réalisé avec succès.
