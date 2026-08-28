# IFC

**Portée :** liaison, reconstruction en éléments natifs, export. Les 20 outils `ifc_*`,
leur nature et leurs défauts connus sont dans `inventaire-des-outils.md`, généré
depuis le code.
**Sources :** les outils `ifc_*`, `inventaire-des-outils.md`.
**Vérifié le :** 2026-08-28

## Ce qu'un import IFC produit

Des **DirectShape** : des volumes sans intelligence Revit. Un mur importé n'est pas un
mur, c'est une forme qui y ressemble — pas de type, pas de couches, pas d'hôte pour
les percements. Toute la « reconstruction » consiste à remplacer ces volumes par des
éléments natifs, catégorie par catégorie.

C'est aussi pourquoi lier un IFC (`ifc_link`) suffit dans la plupart des cas : pour
coordonner avec la structure ou les fluides, on n'a pas besoin d'éléments natifs. La
reconstruction ne se justifie que si l'on doit **reprendre** le modèle.

## Toujours commencer par les capacités

`ifc_get_capabilities` en premier appel IFC de la session : il dit quelles versions
IFC sont prises en charge et si le module `revit-ifc` est présent. Importer un IFC
lourd sans l'avoir demandé, c'est découvrir l'incompatibilité après l'attente.

## Lier ou importer

| Besoin | Outil |
|---|---|
| Référence de coordination | `ifc_link` |
| Recharger un lien existant | `ifc_reload_link` |
| Ouvrir en document Revit, ou importer | `ifc_open_or_import` |

Deux choses à savoir avant de lancer :

- un **fichier `.RVT` intermédiaire est créé à côté du fichier IFC d'origine**. Il
  faut donc un dossier accessible en écriture. `recreateLink: false` réutilise un
  `.RVT` déjà généré au lieu de le refaire ;
- l'option d'import `parametric` donne des éléments plus modifiables, mais l'import
  est plus lent et **la géométrie n'est pas toujours préservée**. Sans elle, tout
  arrive en DirectShape.

## Reconstruire en éléments natifs

Dans cet ordre :

1. `ifc_analyze_rebuildability` avec `compact: true` — classe chaque DirectShape
   reconstructible ou non, avec un **indice de confiance**. Compter 60 à 80 % de
   reconstructible sur un modèle ordinaire ;
2. `ifc_list_rebuild_candidates` avec `compact: true`, filtré par catégorie ;
3. la reconstruction, **une catégorie à la fois**, chacune en `dryRun` d'abord :
   `ifc_rebuild_walls` · `ifc_rebuild_floors` · `ifc_rebuild_roofs` ·
   `ifc_rebuild_structural_members` · `ifc_rebuild_openings` ·
   `ifc_rebuild_family_instances` ;
4. `ifc_compare_original_vs_rebuilt` — rend un score de fidélité ;
5. `ifc_tag_unreconstructable_elements` pour marquer ce qui n'a pas pu l'être, plutôt
   que de le laisser passer pour du modèle abouti.

`ifc_set_family_mapping_file` charge une correspondance de familles sur mesure. À
faire **avant** l'étape 3, pas après. Avant une reconstruction coûteuse,
`ifc_validate_request` valide la demande sans l'exécuter.

### Ce que la reconstruction laisse de côté

| Outil | Comportement à connaître |
|---|---|
| `ifc_rebuild_walls` | **saute** les murs à géométrie non linéaire — courbes, inclinés. Le type est choisi d'après l'épaisseur, tolérance 50 mm |
| `ifc_rebuild_floors` | extrait le profil de la **face inférieure** |
| `ifc_rebuild_openings` | cherche le mur ou le sol hôte par **recouvrement de boîte englobante** |
| `ifc_rebuild_family_instances` | même recherche d'hôte ; **sans mur hôte à moins de 600 mm, l'instance est posée sans hôte** |

Les comptes rendus de `dryRun` disent combien d'éléments sont sautés et pourquoi. Les
lire : un « 95 murs reconstruits » qui tait 25 murs courbes laisse un modèle troué.

## Exporter

| Besoin | Outils |
|---|---|
| Export simple | `ifc_export_basic` |
| Avec configuration | `ifc_list_export_configurations`, puis `ifc_get_export_configuration` et `ifc_export_with_configuration` |

## À éviter

- Reconstruire sans avoir lancé `ifc_analyze_rebuildability`.
- Reconstruire toutes les catégories en une fois, ou en parallèle.
- Reconstruire alors qu'un simple lien suffisait.
- Importer un IFC lourd sans avoir vérifié les capacités.
- Omettre `compact: true` sur les outils d'analyse : leurs réponses sont volumineuses.
- Lire un compte de reconstruction sans lire le compte d'éléments sautés.
