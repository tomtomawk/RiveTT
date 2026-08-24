# Inventaire des outils RiveTT

> Document **généré** par `tools/audit-tool-surface.py`. Ne pas éditer à la main :
> relancer le script après toute modification de la surface d'outils.

Relevé du 2026-08-24 — connecteur 0.2.0 — **295 outils publiés**, 292 classes runtime.

## Comment lire ce document

Deux surfaces sont croisées : les attributs `[McpServerTool]` du serveur MCP et les
classes `ICortexTool` du runtime. La question posée à chaque outil est celle qui a
coûté le plus cher jusqu'ici : **un paramètre publié est-il vraiment lu**.

| Colonne | Ce qu'elle dit |
|---|---|
| Nature | `lecture` ou `écriture` selon `[ToolSafety]`. Depuis le verrou du ruban, ce classement est une frontière de permission, plus une simple étiquette |
| dryRun | l'outil accepte une prévisualisation |
| Int. | intérêt pour une agence d'architecture : **5** geste quotidien, **4** utile régulier, **3** ponctuel, **2** marginal, **1** hors périmètre. Jugement d'usage, pas une propriété du code : il vit dans les listes `TIER5`/`TIER4`/`TIER2` du script et se corrige en les éditant |
| Défaut probable | **critique** et **majeur** vérifiés dans le code ; **signal** détecté automatiquement, avec des faux positifs quand la lecture passe par un helper partagé ou un DTO typé ; **mineur** systémique |

Une flèche `→` signale une **façade** : un nom MCP qui appelle un autre outil runtime.

## Synthèse

| Mesure | Valeur |
|---|---|
| Outils publiés | **295** |
| Dont écriture | **181** (61 %) — c'est la part que le verrou du ruban gouverne |
| Ferraillage et charpente métallique | **112** (38 %), hors périmètre d'une agence d'architecture, chargés à chaque session |
| Écritures sans `dryRun` | **92**, dont **76** hors ferraillage, alors que le contrat annonce `dryRunDefault: true` |
| Erreurs génériques `Failed: …` sans suggestion | **167** |
| Géométrie par boîte englobante | **16** |
| Classement `[ToolSafety]` en désaccord avec le nom | **12** |
| Défauts confirmés / signaux à vérifier | **8** / **16** |

## Répartition par catégorie

| Catégorie | Outils | Part |
|---|---:|---:|
| Rebar | 64 | 22 % |
| Elements | 58 | 20 % |
| StructuralSteel | 48 | 16 % |
| Project | 45 | 15 % |
| IFC | 20 | 7 % |
| Views | 12 | 4 % |
| LinkedFiles | 11 | 4 % |
| Parameters | 8 | 3 % |
| Annotations | 7 | 2 % |
| Sheets | 5 | 2 % |
| Workflows | 5 | 2 % |
| Meta | 4 | 1 % |
| Documents | 4 | 1 % |
| Architecture | 2 | 1 % |
| Interop | 1 | 0 % |
| Code | 1 | 0 % |

Une agence de 37 personnes en logement, équipement, tertiaire et santé n'utilisera
jamais 38 % de cette surface. Ces outils ne sont pas neutres : ils occupent le
catalogue que l'agent lit à chaque session et diluent le choix de l'outil juste.

## Défauts confirmés

Lus dans le code, pas déduits.

| Outil | Gravité | Ce que le code fait |
|---|---|---|
| `batch_create_sheets` | critique | fenêtres placées à (0,5 ft ; 0,5 ft) en dur, alors que l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le cartouche A1 français. |
| `workflow_sheet_set` | critique | `viewIds` est publié dans la spec et jamais lu : les feuilles sortent vides, sans aucun signalement. |
| `delete_material` | majeur | destructif sans dryRun. |
| `delete_schedule` | majeur | destructif sans dryRun. |
| `delete_selection` | majeur | destructif sans dryRun, alors que `delete_element` en a un par défaut. |
| `ifc_set_family_mapping_file` | majeur | classé lecture seule alors qu'il modifie un réglage d'export persistant : il traverse donc le verrou d'écriture du ruban. |
| `send_code_to_revit` | majeur | aucun dryRun sur l'outil le plus puissant. |
| `workflow_clash_review` | majeur | détection par boîtes englobantes alors que `clash_detection` utilise l'intersection solide : l'outil composé rend plus de faux positifs que le simple. |

## Signaux à vérifier

Détection automatique. Un signal n'est pas un défaut : la lecture passe peut-être
par un helper partagé ou un DTO typé, ou la clé annoncée n'est qu'un exemple de
documentation.

| Outil | Signal |
|---|---|
| `add_prefix_suffix` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `create_free_form_rebar` | clé imbriquée annoncée, absente du runtime : end, mid |
| `create_rebar_from_curves` | clé imbriquée annoncée, absente du runtime : arrayLengthMm, spacingMm |
| `create_rebar_from_shape` | clé imbriquée annoncée, absente du runtime : arrayLengthMm, barsOnNormalSide, includeFirstBar, includeLastBar, spacingMm |
| `cross_app_selection` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |
| `detach_wall_constraint` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `duplicate_family_type` | clé imbriquée annoncée, absente du runtime : paramName |
| `duplicate_storey` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `filter_by_parameter_value` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `manage_fabric_rounding` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : applyRules, lengthRoundingMm |
| `manage_rebar_rounding` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : applyRules, lengthRoundingMm |
| `set_element_phase` | clé imbriquée annoncée, absente du runtime : phaseCreatedId, phaseDemolishedId |
| `set_rebar_layout` | clé imbriquée annoncée, absente du runtime : arrayLengthMm, barsOnNormalSide, includeFirstBar, includeLastBar, spacingMm |
| `set_steel_connection_type` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : connectionHandlerTypeId, connectionHandlerTypeName |
| `sync_csv_parameters` | clé imbriquée annoncée, absente du runtime : paramName1 |

## Inventaire complet

### Rebar — 64 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_free_form_rebar` | écriture | oui | 1 | Create an unconstrained free-form rebar from curve loops (mm) in a host. loops is a JSON array of loops, each a JSON array of curve specs {type, start… | **signal** — clé imbriquée annoncée, absente du runtime : end, mid |
| `create_rebar_from_curves` | écriture | oui | 1 | Create a rebar from explicit coplanar curves (mm) in a host. curves is a JSON array of {type:line\|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}; normal… | **signal** — clé imbriquée annoncée, absente du runtime : arrayLengthMm, spacingMm |
| `create_rebar_from_shape` | écriture | oui | 1 | Create a shape-driven rebar in a host from a rebar shape. origin/xVec/yVec are JSON {x,y,z} in mm. Optional layout JSON. | **signal** — clé imbriquée annoncée, absente du runtime : arrayLengthMm, barsOnNormalSide, includeFirstBar, includeLastBar, spacingMm |
| `manage_fabric_rounding` | écriture | — | 1 | Set the document fabric length-rounding rules. Fields: applyRules (bool), lengthRoundingMm (double), lengthRoundingMethod (Nearest\|Up\|Down). volumeRou… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : applyRules, lengthRoundingMm |
| `manage_rebar_rounding` | écriture | — | 1 | Set rebar length-rounding rules. Without rebarId edits the document default; with rebarId edits that bar. Fields: applyRules (bool), lengthRoundingMm… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : applyRules, lengthRoundingMm |
| `set_rebar_layout` | écriture | oui | 1 | Set the distribution layout of a shape-driven rebar. layout is JSON {rule, number?, arrayLengthMm?, spacingMm?, barsOnNormalSide?, includeFirstBar?, i… | **signal** — clé imbriquée annoncée, absente du runtime : arrayLengthMm, barsOnNormalSide, includeFirstBar, includeLastBar, spacingMm |
| `create_rebar_bending_detail` | écriture | — | 1 | Create a rebar bending detail for a rebar in a view (Revit 2024+). Provide rebarId and viewId (a drafting/detail view); optional bendingDetailTypeId,… | **mineur** — pas de dryRun |
| `create_rebar_coupler` | écriture | — | 1 | Create a rebar coupler connecting two bar ends, or cap one. Provide couplerTypeId or couplerTypeName (a Coupler-category family type), end1 (JSON {reb… | **mineur** — pas de dryRun |
| `get_area_reinforcement_data` | lecture | — | 1 | Read an area reinforcement system: major direction (mm vector), type id/name, host id, member rebar ids, boundary curve ids, member count. | **mineur** — erreur générique sans suggestion |
| `get_fabric_area_data` | lecture | — | 1 | Read a fabric area system: type id/name, host id, sheet ids, sheet count, major direction (mm vector). | **mineur** — erreur générique sans suggestion |
| `get_fabric_rounding` | lecture | — | 1 | Read the document fabric length-rounding rules (apply flag, segment/total length rounding in mm and method Nearest\|Up\|Down). | **mineur** — erreur générique sans suggestion |
| `get_fabric_sheet_data` | lecture | — | 1 | Read a fabric sheet: type id/name, isBent, fabricNumber, cut overall length and width (mm). | **mineur** — erreur générique sans suggestion |
| `get_fabric_wire_data` | lecture | — | 1 | Read the wire items of a fabric sheet in one direction. Provide fabricSheetId and direction (major\|minor); optional maxWires (default 200). Returns pe… | **mineur** — erreur générique sans suggestion |
| `get_path_reinforcement_data` | lecture | — | 1 | Read a path reinforcement system: type id/name, host id, member rebar ids, curve element ids, additional offset (mm), primary bar orientation. | **mineur** — erreur générique sans suggestion |
| `get_rebar_bending_detail_data` | lecture | — | 1 | Read a rebar bending detail (Revit 2024+): host rebar id, owner view id, position (mm) and rotation (degrees). Provide bendingDetailId. Returns a vers… | **mineur** — erreur générique sans suggestion |
| `get_rebar_constraint_candidates` | lecture | — | 1 | List the constraint candidates for one rebar handle. Provide rebarId and handleIndex (from manage_rebar_constraints action=list_handles). Read-only. | **mineur** — erreur générique sans suggestion |
| `get_rebar_constraints` | lecture | — | 1 | List the constrained handles of a rebar and whether its constraints can be edited. | **mineur** — erreur générique sans suggestion |
| `get_rebar_coupler_data` | lecture | — | 1 | Read a rebar coupler: couplerMark, quantity, type id/name, and each linked reinforcement descriptor {rebarId, end}. Provide couplerId. | **mineur** — erreur générique sans suggestion |
| `get_rebar_element_data` | lecture | — | 1 | Read a single rebar's core data: bar type, host, shape, layout rule, bar count, total length (mm), volume. | **mineur** — erreur générique sans suggestion |
| `get_rebar_geometry` | lecture | — | 1 | Return the centerline curves (mm) of a rebar at a bar position index (default 0). Optionally suppress hooks/bend radius. | **mineur** — erreur générique sans suggestion |
| `get_rebar_host_data` | lecture | — | 1 | Report reinforcement hosted by an element: validity and the rebar/area/path/fabric it contains, plus common cover. | **mineur** — erreur générique sans suggestion |
| `get_rebar_numbering` | lecture | — | 1 | Read rebar numbering. With rebarId returns that bar's schedule mark; without it returns every rebar's schedule mark plus the count of blank marks (a p… | **mineur** — erreur générique sans suggestion |
| `get_rebar_rounding` | lecture | — | 1 | Read rebar length-rounding rules. Without rebarId returns the document default; with rebarId returns that bar's effective rounding. Method is Nearest\|… | **mineur** — erreur générique sans suggestion |
| `get_rebar_splice_candidates` | lecture | — | 1 | Report candidate splice geometries for a rebar by rules (Revit 2025+, read-only). Provide rebarId, optional spliceTypeId, position (End1\|Middle\|End2).… | **mineur** — erreur générique sans suggestion |
| `get_rebar_splice_data` | lecture | — | 1 | Read rebar splice data (Revit 2025+): for each bar end, lap length (mm), stagger (mm), splice position, connected rebar id/end, plus the splice chain.… | **mineur** — erreur générique sans suggestion |
| `get_rebar_varying_data` | lecture | — | 1 | Read varying-length rebar state (Revit 2025+, read-only): canHaveVaryingLengthBars, varyingEnabled, and per-position centerline length (mm). Provide r… | **mineur** — erreur générique sans suggestion |
| `get_reinforcement_settings` | lecture | — | 1 | Read document-level reinforcement settings. | **mineur** — erreur générique sans suggestion |
| `list_rebar_bar_types` | lecture | — | 1 | List all rebar bar types (id, name, model and nominal diameter in mm). | **mineur** — erreur générique sans suggestion |
| `list_rebar_cover_types` | lecture | — | 1 | List all rebar cover types (id, name, cover distance in mm). | **mineur** — erreur générique sans suggestion |
| `list_rebar_fabric_types` | lecture | — | 1 | List fabric reinforcement types (fabric sheet types and fabric area types). | **mineur** — erreur générique sans suggestion |
| `list_rebar_hook_types` | lecture | — | 1 | List all rebar hook types (id, name, hook angle in degrees). | **mineur** — erreur générique sans suggestion |
| `list_rebar_shapes` | lecture | — | 1 | List all rebar shapes (id, name). | **mineur** — erreur générique sans suggestion |
| `list_rebar_splice_types` | lecture | — | 1 | List rebar splice types (Revit 2025+; returns a version error on older targets). | **mineur** — erreur générique sans suggestion |
| `manage_rebar_constraints` | écriture | — | 1 | Inspect/edit rebar constraints. Provide rebarId and action: list_handles \| list_candidates (with handleIndex) \| set_preferred (with handleIndex, candi… | **mineur** — pas de dryRun |
| `manage_rebar_numbering` | écriture | — | 1 | Manage rebar numbering. action=set_number writes a single bar's schedule mark (needs rebarId + newNumber). action=renumber\|remove_gaps are not exposed… | **mineur** — pas de dryRun |
| `modify_rebar_bending_detail` | écriture | — | 1 | Modify a rebar bending detail (Revit 2024+). Provide bendingDetailId and any of position JSON {x,y,z} in mm, rotationDegrees. Returns a version error… | **mineur** — pas de dryRun |
| `propagate_rebar` | lecture | — | 1 | Reports that rebar propagation is unsupported: the Revit API exposes no propagation method on any supported version. Returns a structured 'unsupported… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `remove_rebar_splice` | écriture destructif | — | 1 | Remove a rebar splice at a bar end (Revit 2025+). Provide rebarId and optional barEnd (0 or 1; default 0). Returns a version error on older targets. | **mineur** — pas de dryRun |
| `set_rebar_coupler_visibility` | écriture | — | 1 | Set a coupler unobscured (solid) or obscured in a view. Provide couplerId, viewId, unobscured (bool). | **mineur** — pas de dryRun |
| `set_reinforcement_settings` | écriture | — | 1 | Set document-level reinforcement settings. Provide any of hostStructuralRebar, rebarShapeDefinesHooks, rebarShapeDefinesEndTreatments (bools). Some to… | **mineur** — pas de dryRun |
| `splice_rebar` | écriture destructif | — | 1 | Splice a rebar by rules at a position (Revit 2025+). Provide rebarId, optional spliceTypeId, position (End1\|Middle\|End2). Returns the resulting rebar… | **mineur** — pas de dryRun |
| `transfer_rebar_annotations` | écriture | — | 1 | Transfer rebar annotations between views by recreating MultiReferenceAnnotations over the rebars visible in the source view. Provide sourceViewId, tar… | **mineur** — pas de dryRun |
| `unify_rebars` | écriture destructif | — | 1 | Unify compatible standalone bars into one (Revit 2025+). Provide rebarIds (JSON array of >=2 ids); bars are unified pairwise into a single rebar. Retu… | **mineur** — pas de dryRun |
| `convert_rebar_system_to_rebars` | écriture destructif | oui | 1 | Convert an area or path reinforcement system into standalone rebars (destructive). Provide systemId. Returns the resulting standalone rebar ids. | — |
| `create_area_reinforcement` | écriture | oui | 1 | Create an area reinforcement system on a host (wall/floor/foundation). majorDirection is JSON {x,y,z}; optional curves is a JSON array of {type:line\|a… | — |
| `create_fabric_area` | écriture | oui | 1 | Create a fabric area system on a host (wall/floor/foundation). majorDirection is JSON {x,y,z}; optional curves is a JSON array of {type:line\|arc, star… | — |
| `create_fabric_sheet` | écriture | oui | 1 | Create a single fabric sheet in a host. Provide hostId and fabricSheetTypeId or fabricSheetTypeName; optional bendProfile is a JSON array of {type:lin… | — |
| `create_path_reinforcement` | écriture | oui | 1 | Create a path reinforcement system on a host. curves is a JSON array of {type:line\|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm (required). Optio… | — |
| `get_rebar_api_capabilities` | lecture | — | 1 | Report which version-gated reinforcement features the running Revit supports. | — |
| `include_exclude_rebar_bars` | écriture | oui | 1 | Show or hide a single bar of a rebar set in a view. Provide rebarId, viewId, barPositionIndex, hidden (true=hide). | — |
| `move_rebar_in_set` | écriture | oui | 1 | Move a single bar within a rebar set by a translation vector (mm). Provide rebarId, barPositionIndex, translation JSON {x,y,z}. Pass reset:true to cle… | — |
| `place_fabric_sheet` | écriture | oui | 1 | Place an existing fabric sheet into a host. Provide fabricSheetId and hostId; optional transform is JSON {translation:{x,y,z}} in mm (default identity… | — |
| `remove_fabric_reinforcement_system` | écriture destructif | oui | 1 | Remove a fabric area reinforcement system (destructive). Provide fabricAreaId. | — |
| `remove_rebar_system` | écriture destructif | oui | 1 | Remove an area or path reinforcement system (destructive). Provide systemId. | — |
| `set_area_reinforcement_layers` | écriture | oui | 1 | Activate or deactivate a layer of an area reinforcement system. Provide areaReinforcementId, layer (top_major\|top_minor\|bottom_major\|bottom_minor) and… | — |
| `set_fabric_sheet_bend_profile` | écriture | oui | 1 | Set the bend profile of a bent fabric sheet. Provide fabricSheetId and bendProfile (a JSON array of {type:line\|arc, start{x,y,z}, end{x,y,z}, mid?{x,y… | — |
| `set_path_reinforcement_options` | écriture | oui | 1 | Set options on a path reinforcement system. Provide pathReinforcementId and any of additionalOffsetMm, primaryBarOrientation (TopOrExterior\|BottomOrIn… | — |
| `set_rebar_hooks` | écriture | oui | 1 | Set the hook type at rebar ends. Provide rebarId and startHookId and/or endHookId (pass 0 to clear an end's hook). Works on all Revit versions. | — |
| `set_rebar_host` | écriture | oui | 1 | Reassign a rebar to a new host. Provide rebarId and newHostId (must be a valid rebar host). | — |
| `set_rebar_shape` | écriture | oui | 1 | Change the shape of a shape-driven rebar. Provide rebarId and shapeId or shapeName. | — |
| `set_rebar_terminations` | écriture | oui | 1 | Set rebar end terminations (orientation/rotation). Revit 2026+ only; returns a version error on older targets. Provide rebarId, end (0\|1), orientation… | — |
| `set_rebar_varying` | écriture | oui | 1 | Enable/disable a varying-length rebar set (the 'Varying Rebar Set' command, Revit 2025+). Provide rebarId and enabled (bool): when true the set's cons… | — |
| `set_rebar_visibility` | écriture | oui | 1 | Set rebar view presentation. Provide rebarId, viewId, and unobscured (show in front of host). | — |
| `split_rebar` | écriture destructif | oui | 1 | Split a shape-driven rebar set into two sets at a given bar position. Provide rebarId and splitAtPosition (1..count-1). Returns the original and new r… | — |

### Elements — 58 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `filter_by_parameter_value` | lecture | — | 5 | Filter elements by one parameter condition, or several combined with AND/OR via the conditions array. Conditions: equals, not_equals, contains, not_co… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `ai_element_filter` | lecture | — | 5 | Paginated element query by category, class, family symbol, bounding box, or level. Returns totalCount, returnedCount, appliedLimit and nextCursor. res… | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; géométrie par boîte englobante |
| `batch_rename` | écriture destructif | oui | 5 | Batch rename elements or system types in the Revit project. Supports both loadable-family elements and system types (wall/floor/ceiling/roof types). | **mineur** — erreur générique sans suggestion |
| `copy_elements` | écriture | — | 5 | Copy elements with optional mm offset. Can target a different view (sourceViewId+targetViewId) or another OPEN document (targetDocumentTitle). | **mineur** — pas de dryRun |
| `create_door` → `create_point_based_element` | écriture | oui | 5 | Place a door family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z… | **mineur** — géométrie par boîte englobante |
| `create_floor` | écriture | — | 5 | Create an architectural floor from a boundary (or a room), optionally with holes. Provide boundaryPoints OR roomId. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_grid` | écriture destructif | — | 5 | Create a grid system (X and/or Y grids by count + spacing), or rename/delete an existing grid. action=create\|rename\|delete. Spacing/extent values are… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_level` | écriture destructif | oui | 5 | Create, edit, rename, or delete a level. action=create\|set\|rename\|delete. For set/rename/delete identify the level by levelId or name. | **mineur** — erreur générique sans suggestion |
| `create_room` | écriture | oui | 5 | Create a room at a point on a level. x/y are plan coordinates in mm; the level sets the elevation. The response reports enclosed and areaM2 - a point… | **mineur** — erreur générique sans suggestion |
| `create_room_separation_line` | écriture | oui | 5 | Draw room separation lines in a plan view to split or bound a room without building a physical wall. path is a JSON array [{x,y,z}, ...] in mm. This i… | **mineur** — erreur générique sans suggestion |
| `create_stair` | écriture | oui | 5 | Create a native component stair between two levels. runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the levels drive the ele… | **mineur** — erreur générique sans suggestion |
| `create_window` → `create_point_based_element` | écriture | oui | 5 | Place a window family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give… | **mineur** — géométrie par boîte englobante |
| `delete_element` | écriture destructif | oui | 5 | Delete elements. The dryRun preview reports the real cascade (dependent tags, sketches, railings...) and any group membership. Deleting a group MEMBER… | **mineur** — erreur générique sans suggestion |
| `edit_group_members` | écriture destructif | oui | 5 | Add or remove members of a model group. The Revit API cannot edit group members in place, so this ungroups the instance, changes the member set and cr… | **mineur** — erreur générique sans suggestion |
| `export_room_data` | lecture | — | 5 | Export room data (area in m2, perimeter, level, department). Filter inside Revit with levelName/levelId and nameFilter instead of returning every room… | **mineur** — géométrie par boîte englobante ; erreur générique sans suggestion |
| `export_to_excel` | lecture | — | 5 | Export element data from a Revit category to an Excel file. | **mineur** — erreur générique sans suggestion |
| `get_current_view_elements` | lecture | — | 5 | List elements visible in the currently active view. categoryFilter is a single-category shortcut (OST code, English name or localized label); modelCat… | **mineur** — erreur générique sans suggestion |
| `get_linked_elements` | lecture | — | 5 | Query elements from linked Revit models with optional filtering. parameterNames is additive — without it only basic fields are returned. | **mineur** — erreur générique sans suggestion |
| `get_selected_elements` | lecture | — | 5 | Get currently selected elements in Revit. | **mineur** — erreur générique sans suggestion |
| `import_from_excel` | écriture destructif | oui | 5 | Import parameter values from an Excel file into Revit elements. | **mineur** — erreur générique sans suggestion |
| `modify_element` | écriture | — | 5 | Move, rotate, mirror, or copy elements. Vectors are {"x":mm,"y":mm,"z":mm} JSON objects. move needs translation; rotate needs rotationCenter + rotatio… | **mineur** — pas de dryRun |
| `create_wall` → `create_line_based_element` | écriture | oui | 5 | Create one native Revit wall. wallTypeId and baseLevelId are required. Set topLevelId to constrain the wall to a level; topOffset is in mm and may be… | — |
| `export_elements_data` | lecture | — | 5 | Export element data as JSON or CSV, by category and/or by explicit elementIds. Parameter names may be given in English or in the document language (Ma… | — |
| `get_element_parameters` | lecture | — | 5 | Get parameters of elements by Revit element ID. Numeric values come back in PROJECT display units with an explicit unit plus the Revit internal value… | — |
| `manage_model_groups` | écriture destructif | oui | 5 | Inventory model groups, duplicate a group type and optionally swap selected instances, or ungroup selected model groups. Write actions preview by defa… | — |
| `renumber_elements` | écriture destructif | oui | 5 | Renumber rooms/doors/windows by location or name. Writes into the specified parameter; supports prefix/suffix and start/increment. | — |
| `set_element_parameters` | écriture destructif | oui | 5 | Set parameter values on one or more elements. Pass requests as a JSON-encoded array string. Supports parameterName by display name and builtInParamete… | — |
| `delete_selection` | écriture destructif | — | 4 | Delete a saved selection filter by name | **majeur** — destructif sans dryRun, alors que `delete_element` en a un par défaut. |
| `duplicate_family_type` | écriture | — | 4 | Duplicate a loadable family type with a new name and optional parameter overrides. | **signal** — clé imbriquée annoncée, absente du runtime : paramName |
| `set_element_phase` | écriture | — | 4 | Assign created/demolished phase to elements. Pass a JSON array of requests: [{elementId, phaseCreatedId?, phaseDemolishedId?}]. | **signal** — clé imbriquée annoncée, absente du runtime : phaseCreatedId, phaseDemolishedId |
| `capture_selection` | lecture | — | 4 | Capture explicit element IDs or the current Revit selection as a reusable temporary token. Tokens expire and are scoped to the active document session… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `change_element_type` | écriture destructif | — | 4 | Change the type of one or more elements to a target type specified by ID or name. | **mineur** — pas de dryRun |
| `color_elements` | écriture | — | 4 | Color the active view's elements of a category by grouping them on a parameter value, or reset (clear) those color overrides. action=color\|reset. Oper… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_array` | écriture | — | 4 | Create a linear or radial array. Default builds a real associative Revit ArrayElement (editable count); set associative=false for loose copies. linear… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_detail_line` | écriture | oui | 4 | Draw 2D detail lines in a view (view-owned, not visible in other views). path is a JSON array [{x,y,z}, ...] in mm; consecutive points become segments… | **mineur** — erreur générique sans suggestion |
| `create_filled_region` | écriture | — | 4 | Create a filled region in a view from a closed boundary, optionally with holes (inner loops). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_model_line` | écriture | oui | 4 | Draw 3D model lines on a horizontal sketch plane. path is a JSON array [{x,y,z}, ...] in mm; all points must share the same z, which sets the plane el… | **mineur** — erreur générique sans suggestion |
| `create_point_based_element` | écriture | oui | 4 | Create point-based elements. Pass [{category, locationPoint:{x,y,z}, typeId?, levelId?, baseLevel?, hostWallId?, facingFlipped?, handFlipped?, rotatio… | **mineur** — géométrie par boîte englobante |
| `create_structural_framing_system` | écriture | — | 4 | Create a beam system on a level over a rectangular area. Default builds a real associative Revit BeamSystem (editable layout); set associative=false f… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_surface_based_element` | écriture | — | 4 | Create surface-based elements (floors, ceilings). Pass [{category, boundary:[{x,y,z}], typeId?, baseLevel?, baseOffset?}]. | **mineur** — pas de dryRun |
| `export_families` | lecture | — | 4 | Export loaded families as .rfa files into a target directory. | **mineur** — erreur générique sans suggestion |
| `find_undimensioned_elements` | lecture | — | 4 | Find elements not referenced by dimensions | **mineur** — erreur générique sans suggestion |
| `find_untagged_elements` | lecture | — | 4 | Find elements without tags in a view | **mineur** — erreur générique sans suggestion |
| `get_element_solid_geometry` | lecture | — | 4 | Get an element's REAL solid geometry (bounding box, centroid, volume m3, face/edge counts AND inferred cross-section shape: circular/rectangular/compl… | **mineur** — erreur générique sans suggestion |
| `get_elements_in_spatial_volume` | lecture | — | 4 | Find elements within a 3D bounding box or room volume. volumeType=room uses volumeIds; volumeType=custom uses customMinX..customMaxZ. | **mineur** — erreur générique sans suggestion |
| `load_family` | écriture | — | 4 | Load a family into the Revit project. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `load_selection` | lecture | — | 4 | Load a saved selection by name, or list the saved selections when name is omitted. | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; erreur générique sans suggestion |
| `match_element_properties` | écriture destructif | — | 4 | Copy parameter values from one source element to one or more target elements. | **mineur** — pas de dryRun |
| `measure_between_elements` | lecture | — | 4 | Measure distance between two elements or two points in mm. Provide either elementId1/elementId2, or point1/point2 (as JSON arrays [x,y,z]). | **mineur** — géométrie par boîte englobante |
| `operate_element` | écriture destructif | — | 4 | Select, highlight, isolate, hide, or zoom to elements. Actions: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, rese… | **mineur** — pas de dryRun |
| `save_selection` | écriture destructif | — | 4 | Save element selection as named filter | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `set_element_workset` | écriture | — | 4 | Move elements to a different workset. Pass a JSON array of requests: [{elementId, worksetName}]. Worksets are resolved by name only. | **mineur** — pas de dryRun |
| `set_material_properties` | écriture destructif | oui | 4 | Set identity, appearance, product info, and asset assignments on Revit materials. Each request is a FLAT object keyed by materialId plus any of: name,… | **mineur** — erreur générique sans suggestion |
| `create_line_based_element` | écriture | oui | 4 | Create line-based elements (walls, beams). Pass a JSON array of specs: [{category, locationLine:{p0:{x,y,z}, p1:{x,y,z}, pMid?:{x,y,z}}, typeId?, heig… | — |
| `get_room_openings` | lecture | — | 4 | Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName. | — |
| `rename_families` | écriture destructif | oui | 3 | Rename loaded families (and optionally their types) with find/replace, prefix, or suffix operations. | **mineur** — erreur générique sans suggestion |
| `detach_wall_constraint` | écriture destructif | oui | 2 | Preview or detach wall top-level constraints or Revit 2027 top/base attachments. Grouped walls are reported and skipped instead of rolling back unrela… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `get_elements_by_unique_id` | lecture | — | 2 | Resolve Revit UniqueId strings to ElementId records for cross-app workflows. | — |

### StructuralSteel — 48 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `set_steel_connection_type` | écriture destructif | oui | 1 | Change a structural connection's type. Revit exposes no in-place type setter, so this recreates the connection: it reads the connected elements, delet… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : connectionHandlerTypeId, connectionHandlerTypeName |
| `analyze_structural_steel_model` | lecture | — | 1 | Document-wide structural steel summary: counts of connection handlers, connection types, connection handler types, approval types, and structural fram… | **mineur** — erreur générique sans suggestion |
| `get_steel_connection_data` | lecture | — | 1 | Read a structural connection handler by id: type id/name, connected element ids, origin, custom/detailed flags, approval type id, code-checking status… | **mineur** — erreur générique sans suggestion |
| `get_steel_connection_input_points` | lecture | — | 1 | Read the input points of a structural connection handler: each point's id (GUID) and position (x,y,z in mm). Provide connectionId. | **mineur** — erreur générique sans suggestion |
| `get_steel_connection_settings` | lecture | — | 1 | Read the document-wide StructuralConnectionSettings (currently exposes the IncludeWarningControls flag). | **mineur** — erreur générique sans suggestion |
| `get_steel_connection_type_data` | lecture | — | 1 | Read a structural connection type by id. Returns StructuralConnectionType (family symbol id, applyTo) or StructuralConnectionHandlerType (connection G… | **mineur** — erreur générique sans suggestion |
| `get_steel_cut_data` | lecture | — | 1 | Read cut relationships for an element: solid-solid cuts (cutting solids + solids being cut via SolidSolidCutUtils) and instance-void cuts (cutting voi… | **mineur** — erreur générique sans suggestion |
| `get_steel_element_properties` | lecture | — | 1 | Read steel fabrication properties of an element: whether it carries SteelElementProperties and its fabrication unique id (GUID). External-id and mater… | **mineur** — erreur générique sans suggestion |
| `get_steel_external_id_map` | lecture | — | 1 | Report the steel fabrication external-id map for an element. The Revit SDK does not expose per-element external-id enumeration; this returns the fabri… | **mineur** — erreur générique sans suggestion |
| `get_steel_fabrication_unique_id` | lecture | — | 1 | Read the steel fabrication unique id (GUID) of an element from its SteelElementProperties. Provide elementId. Returns a note when the element has no s… | **mineur** — erreur générique sans suggestion |
| `get_steel_material_links` | lecture | — | 1 | Report steel fabrication material links for an element. The Revit SDK does not expose linked-material enumeration on SteelElementProperties; this retu… | **mineur** — erreur générique sans suggestion |
| `list_steel_approval_types` | lecture | — | 1 | List StructuralConnectionApprovalType definitions: id, name. Use maxResults (default 100) and summaryOnly for counts-first browsing. | **mineur** — erreur générique sans suggestion |
| `list_steel_connection_handler_types` | lecture | — | 1 | List StructuralConnectionHandlerType definitions: id, name, connection GUID, generic/custom/detailed flags. Use maxResults (default 100) and summaryOn… | **mineur** — erreur générique sans suggestion |
| `list_steel_connection_handlers` | lecture | — | 1 | List structural connection handlers in the document: id, type id/name, connected element count, custom/detailed flags. Use maxResults (default 100) an… | **mineur** — erreur générique sans suggestion |
| `list_steel_connection_types` | lecture | — | 1 | List StructuralConnectionType definitions in the document: id, name, family symbol id, applyTo. Use maxResults (default 100) and summaryOnly for count… | **mineur** — erreur générique sans suggestion |
| `manage_custom_steel_connection_type` | écriture | — | 1 | Mutate a custom structural connection (handler). action = add_references \| remove_references \| add_elements \| remove_subelements. NOTE: Revit's custom… | **mineur** — pas de dryRun |
| `manage_steel_approval_type` | écriture | oui | 1 | Administer StructuralConnectionApprovalType definitions. action = create (requires name) \| list. The Revit API exposes no rename/delete for approval t… | **mineur** — erreur générique sans suggestion |
| `set_steel_fabrication_unique_id` | écriture | — | 1 | Set the steel fabrication unique id (GUID) of an element's SteelElementProperties. Provide elementId and uniqueId (a GUID). The element must already h… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `set_steel_solid_cut_face_splitting` | écriture | — | 1 | Set whether the cutting solid's faces are split at an existing solid cut (SolidSolidCutUtils.SplitFacesOfCuttingSolid). Provide cutElementId, targetEl… | **mineur** — pas de dryRun |
| `add_steel_fabrication_info` | écriture | oui | 1 | Add steel fabrication information to Revit elements so they participate in steel detailing (SteelElementProperties). Provide elementIds as a JSON arra… | — |
| `add_steel_instance_void_cut` | écriture | oui | 1 | Add an instance void cut so a void family instance cuts another element (InstanceVoidCutUtils). Provide voidInstanceId (the cutting void instance) and… | — |
| `add_steel_solid_cut` | écriture | oui | 1 | Add a solid cut so one element cuts another (SolidSolidCutUtils). Provide cutElementId (the cutter) and targetElementId (the element to be cut). Optio… | — |
| `check_steel_cut_eligibility` | lecture | — | 1 | Check whether one element can cut another via a solid cut and/or an instance void cut, without mutating. Provide cutElementId (the cutter) and targetE… | — |
| `create_default_steel_connection_handler_type` | écriture | oui | 1 | Create the default StructuralConnectionHandlerType for the document (CreateDefaultStructuralConnectionHandlerType). Returns the new type id. Supports… | — |
| `create_generic_steel_connection` | écriture | oui | 1 | Create a generic structural connection between two or more elements (works without an installed connection provider — the safe baseline). Provide elem… | — |
| `create_steel_connection` | écriture | oui | 1 | Create a typed structural connection between two or more elements from a connection handler type (connectionHandlerTypeId or connectionHandlerTypeName… | — |
| `create_steel_connection_handler_type` | écriture | oui | 1 | Create a StructuralConnectionHandlerType. Provide name; optional familyName (default empty); optional guid (a new GUID is generated when omitted). Sup… | — |
| `create_steel_structural_connection_type` | écriture | oui | 1 | Create a StructuralConnectionType bound to a family symbol. Provide familySymbolId (a valid connection family symbol); applyTo = BeamsAndBraces \| Colu… | — |
| `delete_steel_connection` | écriture destructif | oui | 1 | Delete a structural connection handler by connectionId. Destructive — supports dryRun to preview. The connected elements themselves are not deleted. | — |
| `get_instance_void_cut_relationships` | lecture | — | 1 | Read the instance-void cut relationships of an element (InstanceVoidCutUtils): cuttingVoidInstances (void instances that cut this element) and element… | — |
| `get_solid_cut_relationships` | lecture | — | 1 | Read the solid-solid cut relationships of an element (SolidSolidCutUtils): cuttingSolids (solids that cut this element) and solidsBeingCut (solids thi… | — |
| `get_steel_connection_applicability` | lecture | — | 1 | Report a StructuralConnectionType's applicability hints. Revit exposes no public 'does this type apply to these elements' predicate, so this returns t… | — |
| `get_steel_connection_validation` | lecture | — | 1 | Report validation warnings for a structural connection handler. The Revit API exposes no public producer of ConnectionValidationInfo for a placed hand… | — |
| `get_steel_element_fabrication_properties` | lecture | — | 1 | Read the steel fabrication properties of an element: whether it has SteelElementProperties (hasFabricationProperties) and its fabrication unique id (G… | — |
| `get_steel_element_warnings` | lecture | — | 1 | Report steel fabrication warnings for an element (or all elements if elementId is omitted). The Revit SDK exposes no steel-specific warning API; this… | — |
| `get_steel_reference_by_fabrication_id` | lecture | — | 1 | Resolve the Revit element referenced by a steel fabrication GUID. Provide fabricationGuid (a GUID). Returns found=true with the referenced elementId,… | — |
| `get_structural_connection_provider_data` | lecture | — | 1 | Report a structural connection provider's metadata/capabilities by id/key. StructuralConnectionsProviderData is an opaque provider-filled buffer with… | — |
| `get_structural_connection_provider_registry` | lecture | — | 1 | Report registered structural connection providers (Autodesk Steel Connections, IDEA StatiCa, etc.). The Revit API exposes no public query on the provi… | — |
| `get_structural_connection_validation_info` | lecture | — | 1 | Report validation detail for a placed structural connection (connectionId). No public API produces a populated ConnectionValidationInfo for an existin… | — |
| `get_structural_steel_api_capabilities` | lecture | — | 1 | Report which structural steel features the running Revit version supports: SteelElementProperties, structural connections, cut utils, custom-connectio… | — |
| `list_steel_connection_providers` | lecture | — | 1 | List installed structural connection providers. The public Revit API exposes no queryable provider registry; this returns count 0 with an explanatory… | — |
| `modify_steel_connection_inputs` | écriture | oui | 1 | Add or remove connected elements on a structural connection handler. action = add_element_ids \| remove_element_ids (provide elementIds[]). add_referen… | — |
| `remove_steel_instance_void_cut` | écriture destructif | oui | 1 | Remove an instance void cut between a void family instance and another element (InstanceVoidCutUtils). Provide voidInstanceId and targetElementId. Gen… | — |
| `remove_steel_solid_cut` | écriture destructif | oui | 1 | Remove a solid cut between two elements (SolidSolidCutUtils). Provide cutElementId and targetElementId. Generic Revit geometry op, not steel-specific. | — |
| `set_steel_connection_approval` | écriture | oui | 1 | Set the approval type of a structural connection handler. Provide connectionId and approvalTypeId or approvalTypeName (verified against the document's… | — |
| `set_steel_connection_default_order` | écriture | oui | 1 | Reset a structural connection handler to its default element order (SetDefaultElementOrder). Provide connectionId. | — |
| `set_steel_connection_status` | écriture | oui | 1 | Set the code-checking status of a structural connection handler. status = NotCalculated \| OkChecked \| CheckingFailed. | — |
| `set_steel_connection_type_family_symbol` | écriture | oui | 1 | Re-bind a StructuralConnectionType to a different family symbol. Provide connectionTypeId and familySymbolId. The new symbol is validated against the… | — |

### Project — 45 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `duplicate_storey` | écriture destructif | oui | 5 | Preview or transactionally duplicate model elements from one level to a target elevation. Reports view-specific, grouped, and constrained dependencies… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `batch_export` | lecture | — | 5 | Export views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats. | **mineur** — classé lecture seule et écrit sur le disque. Volontaire (le modèle n'est pas touché) mais à arbitrer : le verrou n'empêche pas cet écrit. |
| `check_model_health` | lecture | — | 5 | Run a model health check and return a health score. | **mineur** — erreur générique sans suggestion |
| `create_revision` | écriture | — | 5 | List, create, update, or assign revisions to sheets. action=list\|create\|set\|add_to_sheets. 'set' updates an existing revision (needs revisionId). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_schedule` | écriture | — | 5 | Create a new schedule view in Revit. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_sheet` | écriture | oui | 5 | Create a sheet, with a title block. Pass titleBlockId (an OST_TitleBlocks family type id, from list_system_types or get_available_family_types) or a f… | **mineur** — erreur générique sans suggestion |
| `export_schedule` | lecture | — | 5 | Export a schedule as JSON, or write it to a CSV/TSV file. Without exportPath the data comes back inline; with exportPath the file is written using del… | **mineur** — erreur générique sans suggestion |
| `get_available_family_types` | lecture | — | 5 | List available family types in the Revit project. | **mineur** — erreur générique sans suggestion |
| `get_current_view_info` | lecture | — | 5 | Get information about the currently active view in Revit. | **mineur** — erreur générique sans suggestion |
| `get_materials` | lecture | — | 5 | List materials in the active Revit document. nameFilter and materialClass narrow the list inside Revit - a real project carries 200+ materials. | **mineur** — erreur générique sans suggestion |
| `get_project_info` | lecture | — | 5 | Get project name, address, levels, phases, worksets, and links from the active Revit document. | **mineur** — erreur générique sans suggestion |
| `get_schedule_data` | lecture | — | 5 | Export schedule data as JSON from an existing schedule view. availableFields is omitted unless includeAvailableFields=true: it lists every schedulable… | **mineur** — erreur générique sans suggestion |
| `get_warnings` | lecture | — | 5 | Get model warnings from the active Revit document. | **mineur** — erreur générique sans suggestion |
| `get_worksets` | lecture | — | 5 | List all worksets in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `list_schedulable_fields` | lecture | — | 5 | Discover available schedulable fields for a category. | **mineur** — erreur générique sans suggestion |
| `list_system_types` | lecture | — | 5 | List the system types of a category: walls, floors, ceilings, roofs, railings, stairs, ramps, viewports, text, dimensions, sheets, title blocks. Syste… | **mineur** — erreur générique sans suggestion |
| `manage_links` | écriture destructif | — | 5 | List, reload, reload-from-path, unload, or remove linked files. To add a NEW link use add_linked_file instead. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `place_title_block` | écriture | oui | 5 | Place a title block instance on an existing sheet. Use it to repair a sheet that has no frame. Call it without titleBlockId to get the list of title b… | **mineur** — erreur générique sans suggestion |
| `purge_unused` | écriture destructif | oui | 5 | Purge unused families/types and materials, and optionally unreferenced view templates and view filters, from the project. | **mineur** — erreur générique sans suggestion |
| `delete_material` | écriture destructif | — | 4 | Delete a material from the project by ID or name. | **majeur** — destructif sans dryRun. |
| `delete_schedule` | écriture destructif | — | 4 | Delete a schedule by ID or name | **majeur** — destructif sans dryRun. |
| `analyze_model_statistics` | lecture | — | 4 | Analyze element counts by category in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `audit_families` | lecture | — | 4 | Audit families in the Revit project. Lists loadable (.rfa) families by default; set includeSystemFamilies=true to also list system-family types (wall/… | **mineur** — erreur générique sans suggestion |
| `cad_link_cleanup` | écriture destructif | — | 4 | Analyze and clean up imported/linked CAD files. action=list\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `clash_detection` | lecture | — | 4 | Detect clashes between two element categories. Uses true solid-geometry intersection by default (fewer false positives than bounding boxes). | **mineur** — erreur générique sans suggestion |
| `create_material` | écriture | — | 4 | Create a new material in the Revit project. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_preset_schedule` | écriture | — | 4 | Create a schedule from a predefined template (e.g. RoomFinish, DoorHardware, WallQuantities, WindowSchedule). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_material` | écriture | — | 4 | Duplicate an existing material with a new name. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_schedule` | écriture | — | 4 | Duplicate a schedule with a new name | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_system_type` | écriture destructif | — | 4 | Duplicate, rename, or delete a system type (wall, floor, roof, ceiling). action=duplicate\|rename\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_compound_structure` | lecture | — | 4 | Get wall/floor/roof/ceiling layer structure by type ID or name. | **mineur** — erreur générique sans suggestion |
| `get_material_quantities` | lecture | — | 4 | Calculate material area and volume across elements, optionally filtered by category or restricted to the current selection. | **mineur** — erreur générique sans suggestion |
| `get_phases` | lecture | — | 4 | List all project phases in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `get_shared_parameters` | lecture | — | 4 | List all project parameters with their bindings and categories, optionally filtered by category. | **mineur** — erreur générique sans suggestion |
| `lines_per_view_count` | lecture | — | 4 | Count detail lines per view (single document pass, safe on any model size) plus a project-wide model line count. Model lines have no owner view, so th… | **mineur** — erreur générique sans suggestion |
| `manage_additional_settings` | écriture | — | 4 | Manage Additional Settings (Manage tab): line styles, line weights, line patterns, fill patterns, halftone/underlay. | **mineur** — pas de dryRun |
| `manage_phase_filters` | écriture | — | 4 | List, set, or create Revit Phase Filters. Actions: list \| set \| create. The 'set' action changes one presentation (New \| Demolished \| Existing \| Tempo… | **mineur** — pas de dryRun |
| `manage_project_units` | écriture | — | 4 | Get or set project units (length, area, volume, angle, etc.). Actions: get, set, list_valid_units. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_worksets` | écriture destructif | — | 4 | Create, rename, delete, or set the active workset (workshared models only). To LIST worksets use get_worksets. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `modify_schedule` | écriture destructif | — | 4 | Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter,… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `set_compound_structure` | écriture destructif | oui | 4 | Modify compound structure on a wall/floor/roof/ceiling type. action=replace\|add\|remove\|modify\|set_wrapping. set_wrapping sets openingWrapping (none\|ex… | **mineur** — erreur générique sans suggestion |
| `set_project_info` | écriture | — | 4 | Set editable Project Information fields. Only the fields you pass are changed; others are left untouched. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `export_shared_parameter_file` | lecture | — | 3 | Export shared parameter file contents | **mineur** — erreur générique sans suggestion |
| `get_material_properties` | lecture | — | 3 | Get detailed material properties (physical, thermal, appearance) by material ID or name. | **mineur** — erreur générique sans suggestion |
| `list_family_sizes` | lecture | — | 2 | List loaded families with type/instance counts and, when includeSize=true, the family file size in KB measured by exporting each family to a temp file… | **mineur** — erreur générique sans suggestion |

### IFC — 20 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `ifc_export_basic` | écriture | — | 4 | Export the active document to IFC. First-class flags cover the common options; use overrides for any other IFCExportOptions key. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_link` | écriture | — | 4 | Link an IFC file into the active document (creates a .ifc.RVT sidecar file managed by Revit). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_set_family_mapping_file` | lecture | — | 3 | Set the family mapping file used by subsequent IFC exports. | **majeur** — classé lecture seule alors qu'il modifie un réglage d'export persistant : il traverse donc le verrou d'écriture du ruban. |
| `ifc_compare_original_vs_rebuilt` | lecture | — | 3 | Compare volume/geometry between the original DirectShape and its native rebuild. | **mineur** — géométrie par boîte englobante |
| `ifc_export_with_configuration` | écriture | — | 3 | Export using a named configuration (built-in or custom) with optional key/value overrides. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_open_or_import` | écriture destructif | — | 3 | Open or import an IFC file as a native Revit project (actions: open \| import). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_rebuild_family_instances` | écriture | oui | 3 | Place family instances (doors, windows, furniture) from IFC DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_rebuild_openings` | écriture | oui | 3 | Cut openings in rebuilt walls/floors based on IFC opening DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_reload_link` | écriture destructif | — | 3 | Reload an existing IFC link, optionally from a new file. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_tag_unreconstructable_elements` | écriture destructif | — | 3 | Tag IFC DirectShapes that cannot be rebuilt by writing a marker parameter. | **mineur** — pas de dryRun |
| `ifc_analyze_rebuildability` | lecture | — | 3 | Analyze IFC DirectShapes and score feasibility of rebuilding them as native Revit elements. | — |
| `ifc_get_capabilities` | lecture | — | 3 | Detect IFC version support and revit-ifc add-in presence | — |
| `ifc_get_export_configuration` | lecture | — | 3 | Get full details of a specific export configuration by name. | — |
| `ifc_list_export_configurations` | lecture | — | 3 | List available built-in export configurations | — |
| `ifc_list_rebuild_candidates` | lecture | — | 3 | List elements above a rebuild confidence threshold. | — |
| `ifc_rebuild_floors` | écriture | oui | 3 | Rebuild native floors from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_roofs` | écriture | oui | 3 | Rebuild native roofs from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_structural_members` | écriture | oui | 3 | Rebuild columns and beams from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_walls` | écriture | oui | 3 | Rebuild native walls from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_validate_request` | lecture | — | 3 | Validate IFC file path, extension, and schema version. | — |

### Views — 12 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `apply_view_template` | écriture | — | 5 | List, apply, or remove view templates from views. action=list\|apply\|remove. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_view` | écriture | — | 5 | Create a new view in Revit: floor plan, ceiling plan, section, elevation, drafting, or 3D view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_view_filter` | écriture | — | 5 | Create, apply, or list parameter-based view filters. action=create\|apply\|list. A filter carries one rule (parameterName/filterRule/filterValue) or sev… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_view` | écriture | — | 5 | Duplicate an existing view in Revit. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_view_templates` | écriture destructif | — | 5 | List, duplicate, delete, or rename view templates. action=list\|duplicate\|delete\|rename. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `override_graphics` | écriture | — | 5 | Override element graphics in a view (colors, transparency, halftone, line weight). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `place_viewport` | écriture | — | 5 | Place a view on a sheet as a viewport. positionX/positionY are the viewport CENTRE in mm in sheet coordinates; omit both to centre it on the sheet. Th… | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `batch_modify_view_range` | écriture | — | 4 | Modify view range offsets (top, cut plane, bottom, view depth) for multiple views. Offsets are in mm. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_views_from_rooms` | écriture | — | 4 | Create callout, section, or elevation views from rooms with a naming pattern. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `manage_unplaced_views` | écriture destructif | oui | 4 | List or delete views that are not placed on any sheet | **mineur** — erreur générique sans suggestion |
| `section_box_from_selection` | écriture | — | 4 | Create a 3D section box from selected elements | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `rename_views` | écriture destructif | oui | 3 | Batch rename views using find/replace, prefix, or suffix operations. | **mineur** — erreur générique sans suggestion |

### LinkedFiles — 11 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `add_linked_file` | écriture | — | 5 | Adds a new Revit linked file from a file path and optionally places an instance at the given position. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_link_transform` | lecture | — | 4 | Returns the full transform of a linked file instance. | **mineur** — erreur générique sans suggestion |
| `get_linked_file_instances` | lecture | — | 4 | Lists all linked Revit files grouped by type, with transforms and load status. | **mineur** — erreur générique sans suggestion |
| `align_link_to_host` | écriture | — | 2 | Aligns a link instance to the host project's internal origin, shared coordinates, or project base point. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_coordination_models` | lecture | — | 2 | Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances. | **mineur** — erreur générique sans suggestion |
| `get_selected_linked_elements` | lecture | — | 2 | Returns info about currently selected link instances. | **mineur** — erreur générique sans suggestion |
| `highlight_linked_element` | écriture | — | 2 | Highlights an element inside a linked model with an optional section box. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `move_link_instance` | écriture | — | 2 | Moves a linked file instance. mode=delta applies (x,y,z) as an offset; mode=absolute places the origin at (x,y,z). Values are in mm. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `pin_unpin_link_instance` | écriture | — | 2 | Pins or unpins linked file instances. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `reload_linked_file_from` | écriture destructif | — | 2 | Reloads a linked Revit file from a different file path. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `show_cross_model_elements` | écriture | — | 2 | Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc… | **mineur** — pas de dryRun ; erreur générique sans suggestion |

### Parameters — 8 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `bulk_modify_parameter_values` | écriture destructif | oui | 5 | Bulk modify parameter values across elements by category. Supports set, find-and-replace, and other operations. | **mineur** — erreur générique sans suggestion |
| `manage_project_parameters` | écriture destructif | oui | 5 | Manage project parameters. Actions: list \| create \| delete \| modify \| set_group \| set_binding_type \| rename. 'delete' now correctly removes non-shared… | **mineur** — erreur générique sans suggestion |
| `add_prefix_suffix` | écriture destructif | oui | 4 | Add a prefix and/or suffix to parameter values across the model or a selection. Runs as a dry-run preview by default; set dryRun=false to apply the ch… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | écriture destructif | oui | 4 | Clear parameter values on elements by category or scope | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `add_shared_parameter` | écriture | — | 4 | Add a shared parameter to project categories. The data type of a newly created definition is honored (a typed shared parameter, not always Text). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_global_parameters` | écriture destructif | — | 4 | Manage global parameters (project-level named values). Actions: list \| get \| create \| set \| delete \| rename \| set_formula \| move_up \| move_down \| sort… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `transfer_parameters` | écriture destructif | oui | 4 | Copy parameter values from source element to one or more target elements. | **mineur** — erreur générique sans suggestion |
| `sync_csv_parameters` | écriture destructif | oui | 2 | Synchronize parameter values from CSV data into Revit elements. | **signal** — clé imbriquée annoncée, absente du runtime : paramName1 |

### Annotations — 7 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_dimensions` | écriture | — | 5 | Create dimension annotations in the active view. Pass a JSON array of dimension specs. Element mode: [{viewId, referenceIds:[...], linePoint:{x,y,z},… | **mineur** — pas de dryRun |
| `create_text_note` | écriture | — | 5 | Create text notes in a view. Pass a JSON array: [{text, position:{x,y,z}, viewId?, textNoteTypeId?, width?, horizontalAlignment?, verticalAlignment?,… | **mineur** — pas de dryRun |
| `tag_rooms` | écriture | — | 5 | Tag rooms in the active view. Operates on the active view only — activate the correct view first. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_color_legend` | écriture | — | 4 | Color elements by parameter value and optionally create a legend view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `import_table` | écriture | — | 4 | Import a CSV/TSV file as a formatted table in a drafting or legend view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `tag_walls` | écriture | — | 4 | Tag walls at their midpoints in the active view. Operates on the active view only. Tags all walls by default, or a subset via wallIds. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `wipe_empty_tags` | écriture destructif | oui | 4 | Find and remove empty or orphaned tags | **mineur** — erreur générique sans suggestion |

### Sheets — 5 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `batch_create_sheets` | écriture | — | 5 | Create multiple sheets with title blocks and optional view placement. sheets is a JSON array: [{number, name, titleBlockName?, viewIds?}]. | **critique** — fenêtres placées à (0,5 ft ; 0,5 ft) en dur, alors que l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le cartouche A1 français. |
| `align_viewports` | écriture | — | 4 | Align viewports across sheets. 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same region lin… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_placeholder_sheets` | écriture destructif | — | 4 | Create, list, convert, or delete placeholder sheets. action=create\|list\|convert\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_sheet_with_content` | écriture | — | 4 | Duplicate a sheet including annotations and detail items | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_sheet_with_views` | écriture | — | 4 | Duplicate a sheet N times with configurable view duplication options. | **mineur** — pas de dryRun ; erreur générique sans suggestion |

### Workflows — 5 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `workflow_sheet_set` | écriture | — | 4 | Auto-create a set of sheets with title blocks from a definition list: [{number, name, viewIds?}]. | **critique** — `viewIds` est publié dans la spec et jamais lu : les feuilles sortent vides, sans aucun signalement. |
| `workflow_clash_review` | écriture | — | 4 | Detect clashes between two categories and create a 3D section-boxed view for visual review. | **majeur** — détection par boîtes englobantes alors que `clash_detection` utilise l'intersection solide : l'outil composé rend plus de faux positifs que le simple. |
| `workflow_data_roundtrip` | lecture | — | 4 | Export parameters to Excel for external editing, then re-import once the file has been saved. | **mineur** — même cas que `batch_export` : écrit un .xlsx en mode lecture seule. |
| `workflow_model_audit` | lecture | — | 4 | Run a complete model audit workflow. | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; erreur générique sans suggestion |
| `workflow_room_documentation` | écriture | — | 4 | Auto-generate callout views (and optionally sections) for every room on a level. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |

### Meta — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `get_server_capabilities` | lecture | oui | 5 | Report RiveTT's effective automatic-mode, dry-run, audit, response, selection, document, and lifecycle capability contract. | — |
| `clear_cache` | lecture | — | 4 | Clear every entry from the plugin-side tool-result cache. | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `get_cache_stats` | lecture | — | 4 | Return diagnostic hit/miss telemetry from the plugin-side tool-result cache. | — |
| `say_hello` | lecture | — | 2 | Test MCP connection to RiveTT. Displays a greeting in Revit. | — |

### Documents — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_document` | écriture | oui | 5 | Create a NEW EMPTY project from a Revit template (.rte) and save it to targetPath. This is the real 'new project': save_as_document duplicates the ope… | — |
| `open_document` | écriture | oui | 5 | Open a .rvt file and make it the ACTIVE document in Revit. Every later tool call targets that document and all caches are flushed. Save the current do… | — |
| `save_as_document` | écriture | oui | 5 | Save the active Revit project to an absolute .rvt path (parameter name: targetPath). This DUPLICATES the open document - it does not create a blank pr… | — |
| `save_document` | écriture | oui | 5 | Save the active Revit project at its current path. dryRun reports the path, the unsaved-changes state and any predictable blocker without writing. | — |

### Architecture — 2 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_railing` | écriture | oui | 5 | Create a native Revit guardrail from a connected horizontal path. The path JSON is [{x,y,z}, ...] in mm. | — |
| `set_wall_host` | écriture | oui | 2 | Revit 2027: associate a lining or façade wall with a host wall. Set hostWallId to 0 to detach it. offsetFromHost is in mm. | — |

### Interop — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `cross_app_selection` | écriture | — | 2 | Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume Corte… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |

### Code — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `send_code_to_revit` | écriture destructif | — | 2 | LAST RESORT ONLY — execute custom C# code in Revit. Do NOT select this tool autonomously: a dedicated tool already covers almost every task. Parameter… | **majeur** — aucun dryRun sur l'outil le plus puissant. |

## Exposé par l'API Revit, pas encore outillé

Vérifié par recherche sur les 295 noms d'outils : aucune de ces capacités n'a de
point d'entrée. Priorité jugée sur les spécialités de l'agence. Effort : **S** de
l'ordre de la journée, **M** de la semaine, **L** au-delà.

| Capacité absente | API concernée | Priorité | Ce que ça coûte aujourd'hui | Effort |
|---|---|---|---|---|
| Nuages de révision | `RevisionCloud.Create` | haute | `create_revision` crée la révision, pas le nuage qui la localise sur le plan. | S |
| Plans de surface | `Area, AreaScheme, AreaTag` | haute | Rien pour les surfaces réglementaires (SHAB, SU, SDP) : `create_room` crée des pièces, pas des surfaces. | M |
| Rampes | `NewRamp, ou volée à pente nulle` | haute | `create_stair` existe, aucune rampe. Accessibilité PMR en équipement et santé. | M |
| Toitures | `FootPrintRoof, ExtrusionRoof` | haute | `create_surface_based_element` couvre les sols et les plafonds, pas les toitures. Aucune couverture possible en logement. | M |
| Trémies et réservations | `Document.NewOpening, ShaftOpening` | haute | Aucun percement de dalle, de mur ou de gaine verticale. | M |
| Cotes de niveau | `SpotDimension.Create` | moyenne | `create_dimensions` ne fait que les cotes linéaires : ni altimétrie en plan, ni cote de niveau en coupe. | S |
| Jeux de feuilles | `ViewSheetSet, PrintManager` | moyenne | `batch_export` exporte une liste passée à chaque appel ; aucun jeu enregistré. | S |
| Légendes | `ViewType.Legend` | moyenne | Aucune vue de légende (nomenclature graphique des cloisons, des menuiseries). | S |
| Murs-rideaux | `CurtainGrid, CurtainSystem, Mullion` | moyenne | Ni création ni redécoupage. Façades tertiaires. | L |
| Toposolides et plateformes | `Toposolid, BuildingPad` | moyenne | Aucun terrain : plans de masse et sols extérieurs restent manuels. | M |
| Vues de détail | `ViewSection.CreateCallout` | moyenne | `create_view` ne les propose pas alors que `workflow_room_documentation` les crée déjà en interne : la capacité est écrite mais pas exposée. | S |
| Zones de délimitation | `OST_VolumeOfInterest` | moyenne | Cadrage coordonné des vues, dès qu'un plan est découpé sur plusieurs feuilles. | S |
| Synchronisation centrale | `Document.SynchronizeWithCentral` | à arbitrer | `manage_worksets` gère les sous-projets, pas la synchronisation. Structurant à 37, mais une synchro déclenchée par un agent demande une décision explicite. | M |
| Assemblages et pièces | `AssemblyInstance, PartUtils` | basse | Préfabrication et découpe : peu d'usage en conception. | L |
| Images et fonds de plan | `ImageType, ImageInstance` | basse | Impossible d'insérer un relevé scanné ou un fond de géomètre. | S |
| Lignes de raccord | `Matchline, ViewBreak` | basse | Grands linéaires découpés sur plusieurs feuilles. | S |
| Nomenclatures de clés | `ScheduleDefinition en mode clé` | basse | Finitions par pièce, typologies de logement. | M |
| Options de conception | `DesignOption, DesignOptionSet` | basse | `get_server_capabilities` détecte leur présence, aucun outil ne les gère. | M |
| Repères de texte | `KeynoteTag et table de repères` | basse | Annotation normalisée par référence plutôt que texte libre. | M |

Les nuages de révision, les cotes de niveau, les vues de détail et les zones de
délimitation sont quatre efforts **S** sur des gestes quotidiens. Les toitures, les
surfaces réglementaires, les rampes et les trémies sont quatre manques structurels :
sans eux, une maquette de logement ne peut pas être produite de bout en bout par le
connecteur.
