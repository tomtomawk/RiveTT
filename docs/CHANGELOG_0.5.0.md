# RiveTT 0.5.0 — développement

Branche : `codex/dev/0.5.0`. Analyse des recettes du 31 août et du retour de bugs
MCP du 4 septembre 2026. Les résultats ci-dessous distinguent les corrections
du code des comportements qui restent à vérifier dans Revit.

## Navigation et commandes disponibles verrou fermé

- `activate_view(viewId, dryRun=false)` active une vue ou feuille du document
  courant et retourne son identifiant réel. Refuse les gabarits et vues internes.
  L'appel passe par ExternalEvent, sans transaction.
- `open_file(filePath, detachFromCentral=false, dryRun=false)` ouvre et active
  RVT, RFA et RTE. Pour RFT, crée une famille ; pour IFC, convertit un projet.
  Ces deux derniers passent par une copie de travail unique sous
  `%TEMP%\RiveTT\OpenedFiles`, à enregistrer sous dans le dossier du projet.
  Le fichier source n'est pas écrasé. Un IFC est copié avant conversion pour
  isoler aussi les caches de l'importeur. DWG, PDF et images restent des imports
  ou liens dans un document, pas des documents Revit autonomes.
- `open_document` délègue à cette ouverture commune, avec son défaut historique
  `dryRun=true`. `open_family` et `open_template` restent disponibles et passent
  également verrou fermé. Les délais d'ouverture sont de 300 secondes pour
  `open_file` et `open_document`.
- `get_server_capabilities.commandsAvailableWhenLocked` regroupe les commandes
  et les actions accessibles. Les branches `list`/`get` explicitement déclarées
  des outils mixtes et `manage_view_display(action=select)` sont accessibles ;
  leurs écritures restent verrouillées. Une action inconnue ou une enveloppe
  ne correspondant pas au contrat ne reçoit aucune exemption.
- `execution.toolReadOnly` qualifie désormais l'appel et son action.
  `execution.writesAllowed` reste le verrou de session ; aucun outil ne le lève.
  Le verrou protège les modifications du modèle existant, pas toute création
  de fichier : conversion et copies de travail peuvent écrire sur disque.
- L'ouverture d'un document en arrière-plan ne remplace plus le document actif
  mémorisé. Une activation de vue, y compris manuelle, invalide les caches.

## Recettes et correctifs

### Skill unifié installé

La documentation produit est désormais un seul SKILL.md autonome, issu de la
version unifiée. Les variantes LITE et UNIFIED, le routeur et les références
séparées sont supprimés. La configuration des clients et l'inventaire des outils
sont intégrés ; l'audit actualise sa section balisée sans recréer de dossier.
Le ZIP Claude contient uniquement rivett/SKILL.md ; les mises à jour retirent
les anciens fichiers documentaires connus sans effacer le dossier personnel.
Préparation du ZIP vérifiée directement, sans recompilation ni packaging global.

### Bloc architecture du 7 septembre 2026

Premier lot : paramètres JSON optionnels, niveaux des surfaces et résultat appliqué,
séquences d'axes, pièces non fermées et volumes, épaisseurs des types, ouvertures
dédupliquées avec unités, métadonnées des réponses compactes, éléments ignorés et
indication des familles chargeables exclues. Les descriptions MCP sont alignées.
Le suivi détaillé, la migration de baseLevel vers baseElevationMm pour les anciens
appels en altitude et les défauts encore ouverts figurent dans
[le suivi de recette](references/suivi-recette-2026-09-07.md).
Tests automatisés : 586 réussis, 2 ignorés. Builds 2026 et 2027 réussis.
Validation en session Revit encore requise ; aucun installateur régénéré.

### Fenêtre État RiveTT

Le mode affiche explicitement Lecture seule, Écriture autorisée ou Indisponible.
Le document actif et la disponibilité de RiveTT restent visibles ; le canal nommé,
le compteur d'outils et l'origine technique du mode disparaissent. La version passe
en pied de page, le titre RiveTT n'est plus doublé et le journal d'activité conserve
son accès dans l'Explorateur. Aucun changement du verrou ou du journal d'audit.
Contenu couvert par tests unitaires ; rendu natif à vérifier dans Revit (§6 du 0.3.0).

### Configuration des applications dans l'installateur

Deux cases regroupent désormais connexion et skill : « Configurer pour Claude
(config + skill) » et « Configurer pour ChatGPT (config + skill) ». ChatGPT Desktop
reçoit la configuration locale partagée avec Codex et le skill personnel ; un ancien
dossier de skill existant est réutilisé. Claude reçoit sa configuration MCP et un
ZIP avec SKILL.md et ses références, à importer dans Personnaliser → Skills.
La page finale distingue connexion configurée, skill installé et import manuel restant.
La désinstallation ne supprime plus récursivement le dossier personnel du skill.

Validation : tests de préparation/mise à jour/échec du ZIP sur dossiers temporaires
et contrat des tâches. Le parcours visuel de l'installateur, l'import dans Claude et
la découverte du skill dans ChatGPT restent à vérifier lors du prochain packaging.
Aucun installateur n'a été reconstruit pour cette modification.

| Signalement | Traitement dans le code | Validation restante |
|---|---|---|
| Prévisualisations modifiantes, tableaux JSON vides, objets de modification refusés | Correctifs déjà présents dans la base 0.4.0 (3f94880), conservés | Rejouer la recette complète |
| `baseLevel:608` devient une altitude de 608 mm | Séparation identifiant/altitude, refus d'un niveau introuvable | Mur sur niveau réel 608, puis autre niveau |
| Cotes nulles/négatives ou non accrochées | Références de géométrie originales, faces parallèles à la mesure, projection dans la vue ; régénération et validation des mesures ; rollback par cote, y compris ses lignes auxiliaires | Murs parallèles, familles, plans/coupes, points verticaux et diagonaux |
| Annotation créée mais invisible | Avertissements de recadrage pour cotes et tags de pièces, marges d'annotation corrigées par l'échelle | Recadrage actif, marges, vues scindées ou contours non rectangulaires |
| Suppression parfois bloquée | Gestionnaire d'échecs installé après le démarrage des transactions ; avertissements et erreurs Revit exposés | Reproduire la suppression problématique ; cause unique du timeout non démontrée |
| Catégories françaises non résolues | Résolveur commun dans `find_untagged_elements` et `list_schedulable_fields`, erreurs explicites et `categoryBic` | Maquettes FR et EN |
| En-têtes répétés dans les données de nomenclature | Champs visibles, indices réels de cellules ; exclusion de l'en-tête exact seulement si cellules texte, sans supprimer une vraie donnée identique | Nomenclatures simples et avec en-têtes groupés |
| Paramètres compacts perdant unités ou valeurs vides | Conservation de toutes les entrées, unités, valeurs internes et résolution des noms | Lecture d'un échantillon réel |
| Faux blocage de prévisualisation d'enregistrement | Suppression du test exclusif qui confondait le propre handle de Revit avec un verrou tiers | RVT ouvert local et réseau |
| Enregistrement de famille | `save_as_document` accepte RFA pour une famille et RVT pour un projet | Famille issue de RFT |
| Serveurs fantômes sous MSIX | Installateur refusé en contexte empaqueté ; détection et signalement des copies virtualisées existantes | Lancement depuis contexte empaqueté et depuis Explorateur ; les anciennes copies ne sont pas effacées automatiquement |

### Migration de `create_line_based_element`

Cette migration est remplacée par la rupture de contrat documentée dans
`CHANGELOG_0.5.1.md` : `baseLevel` est refusé. Utiliser `baseLevelId` pour un
identifiant Revit et `baseElevationMm` (Z absolu projet, en mm) pour une altitude.

## Corrections issues de la documentation API

1. `Transaction.Start()` réinitialise les options de traitement des échecs.
   Les 190 configurations restantes du runtime sont maintenant installées après
   `Start`, avec garde et test de couverture du code source. Cela concerne aussi
   les prévisualisations et l'exécution Roslyn. [Autodesk — Transaction.Start](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/1146fa87-127d-c432-0f51-79a5eb102031.htm).
2. `RevitLinkType.Reload`, `Unload` et `LoadFrom` ne s'exécutent pas dans une
   transaction extérieure ; ces transactions ont été retirées de `manage_links`.
   La réponse signale l'effacement de l'historique Annuler et contrôle le résultat
   du chargement. [Reload](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/86830eb9-5ddb-c342-fddd-6963e5c03564.htm),
   [Unload](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/83f4add7-1c0a-ddfa-b8ab-5be6df0f28a2.htm),
   [LoadFrom](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/bdb3a91e-9a0a-e68d-51da-c460535f5fd2.htm).
3. `GetInstanceGeometry()` retourne des copies inadaptées aux références de
   cotation ; les cotes utilisent les références de `GetSymbolGeometry()` sans
   transformation de la géométrie, en transformant seulement les normales.
   [Autodesk — GeometryInstance](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/22d4a5d4-dfc2-7227-2cae-b989729696ec.htm).
4. Navigation : [ActiveView](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b6adb74b-39af-9213-c37b-f54db76b75a3.htm),
   [OpenAndActivateDocument](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/3b3d671d-47ec-2ed8-1818-a7c19d01884b.htm),
   [NewFamilyDocument](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/bc292c96-bc2b-04ab-726b-575d92be61fd.htm),
   [OpenIFCDocument](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/596a3b91-4647-a3b6-818f-17f722f13c53.htm).
5. Les marges de recadrage d'annotation sont exprimées à l'échelle papier.
   [Autodesk — marge d'annotation](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/3858d0de-caaa-8e31-545c-8c0a3c8fb0f2.htm).

## Contrôles

Les tests automatisés couvrent les contrats serveur/runtime, le verrou par action,
les formats, la résolution des chemins, les helpers de cotation, de niveau et de
nomenclature, le format compact et l'ordre de configuration des transactions.
Les tests purement UI sont ignorés car `RevitAPIUI.dll` nécessite une session native.
Les comportements géométriques, dialogues, conversions et transactions restent à
rejouer dans Revit 2026.5 et 2027 ; aucune installation ni recette en session réelle
n'a été effectuée pour cette modification. Voir aussi `CHANGELOG_0.3.0.md`, §6.

Contrôles exécutés le 7 septembre 2026 :

- `dotnet build RiveTT.sln -c Release` : réussi, zéro avertissement et erreur.
- `builder/build.ps1` : compilation, publication et tests des deux cibles réussis.
  **547 tests réussis et 2 tests UI ignorés par cible** ; aucun échec.
- `python tools/audit-tool-surface.py` : inventaire installé régénéré, 200 outils
  MCP / 197 handlers runtime (certaines façades partagent leur handler).
- `git diff --check` : aucune erreur ; UTF-8 et BOM des scripts vérifiés.
- Installateur produit : `dist/RiveTT-Setup-0.5.0.exe`, environ 42,1 Mo,
  non signé. Aucun déploiement effectué.

La dernière revue a aussi corrigé `manage_selection(action=list)` : les paramètres
propres au chargement d'une sélection ne peuvent plus déclencher de sélection UI.
