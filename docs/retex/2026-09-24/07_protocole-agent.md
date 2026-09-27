# 07 — Protocole de l'agent qui pilote Revit avec RiveTT

À lire au début de chaque session. Complète le `SKILL.md` de RiveTT et la charte de dessin.

## 1. Démarrage

1. `get_project_info` : document actif, niveaux, verrou (`writesAllowed`).
2. **Si le document actif est un projet réel** et que la demande est un test : ne rien modifier, créer un document séparé (`create_document`), rappeler d'enregistrer le projet réel.
3. **Si le verrou est fermé** : le dire et demander « Compléments → RiveTT → Écriture ». Aucun outil ne le lève. Continuer les lectures en attendant.
4. Charger la charte et les fichiers 05 et 06. **Les relire avant chaque phase de conception**, pas seulement au début : le contenu lu sort du contexte au fil de la session.

## 2. Phases et niveau de réflexion

| Phase | Contenu | Réflexion | Livrable avant de passer à la suite |
|---|---|---|---|
| 0. Programme | Mix, surfaces, PMR, locaux communs, contraintes | Élevée | Hypothèses validées par l'utilisateur |
| 1. Esquisse | Schéma des plans (widget ou JSON, voir 08) | Élevée | Schéma validé |
| 2. Contrôle | Règles de 05 appliquées au schéma | Élevée | Rapport de conformité |
| 3. Construction | Création dans Revit | Faible | Modèle + capture de contrôle |
| 4. Vérification | Captures, `list_warnings`, validateur | Moyenne | Écarts corrigés ou signalés |

Ne jamais modéliser un plan de logement sans programme ni schéma validé : sur la session de test, cela a coûté 3 versions complètes.

## 3. Carte des outils par tâche

Les outils sont chargés à la demande : `tool_search` avec le nom exact, puis appel.

| Tâche | Outils dédiés | `send_code` si… |
|---|---|---|
| Projet | `create_document`, `get_project_info`, `save_document`, `activate_view` | — |
| Types et matériaux | `list_system_types`, `list_family_types`, `duplicate_system_type`, `set_compound_structure`, `create_material` | Couche à modifier par index |
| Murs | `create_wall`, `create_line_based_element`, `detach_wall_constraint` | Plus de ~10 murs d'un coup, attache au toit |
| Dalles, toits | `create_floor`, `create_surface_based_element` | Toit à pentes par côté |
| Menuiseries | `create_door`, `create_window` | Pose en série avec recherche d'hôte |
| Escaliers | `create_stair` (voir 04 § 1.1) | Recalage des volées |
| Garde-corps | `create_railing` | Série de balcons |
| Pièces | `create_room`, `create_room_separation_line`, `tag_rooms`, `renumber_elements` | Paramètres ARC_PAR_* en série, étiquette d'un type donné |
| Surfaces réglementaires | `manage_area_plans` | — |
| Contrôles | `list_warnings`, `check_model_health`, `find_untagged_elements`, `get_room_openings`, `export_room_data`, `detect_clashes` | — |
| Visuel | `batch_export` (IMAGE) — puis `capture_view` quand il existera (voir 03) | — |
| Terrain | `create_toposolid` | Déformation (noue, talus) |
| Sélection utilisateur | `get_selected_elements` | Lecture de la géométrie |

Règle : outil dédié d'abord ; `send_code` pour les séries ou ce qu'aucun outil ne couvre.

## 4. Règles d'écriture avec `send_code_to_revit`

- **Structurer le script par sections** avec un compteur et un journal par section ; retourner un bilan chiffré (créés / attendus, échecs avec coordonnées).
- **Un script par phase**, pas un script géant : une erreur dans le mobilier ne doit pas annuler les murs.
- **Pas d'E/S, pas de réflexion** (refusés par le filtre) ; éviter `GetType()` même dans les journaux.
- **Unités** : fonction `F(mm) = mm / 304.8` systématique ; coordonnées écrites en mm.
- **Tableaux** : `double[,]` plutôt que tableaux implicites mixtes.
- **Après création de murs** : `Regenerate()` avant de lire `Orientation`.
- **Altitude** : Z = 0 pour les familles basées sur un niveau, altitude absolue pour les familles hébergées (voir 04 § 2.2).
- **Emprise** : solides visibles, jamais `get_BoundingBox` (voir 06).
- **Rechercher les types par nom exact, puis par préfixe** si null, et journaliser le type retenu.

## 5. Contrôles obligatoires après chaque écriture importante

1. Bilan du script : créés / attendus, pièces non fermées = 0, éléments hors pièce = 0.
2. `list_warnings` : relire, corriger ou signaler.
3. Capture d'un plan par niveau modifié + une 3D (dès que `capture_view` existe) et examen critique : cohérence, éléments flottants, doublons, espaces extérieurs devant les portes-fenêtres.
4. Checklist de 05 § 8 pour tout logement créé ou modifié.
5. `save_document`.

## 6. Lecture des demandes utilisateur

- **Sélection** : examiner la géométrie avant d'interpréter (4 lignes fermées = contour, pas axe).
- **Dimensions non données** : proposer un profil par défaut chiffré et le faire valider en une question.
- **« Améliore »** : reprendre toutes les remarques précédentes et la checklist, pas seulement la dernière demande.

## 7. Communication

- Dire ce qui a été vérifié et ce qui ne l'a pas été (ex. « non contrôlé visuellement »).
- Chiffrer les résultats (surfaces, nombres, écarts).
- Signaler les défauts restants plutôt que de les taire ; ne pas présenter un plan comme conforme sans passage de la checklist.
- Signaler les bugs RiveTT rencontrés, avec contournement, pour le développeur.
