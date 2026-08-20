# MCPRVTT27 — constats et pistes d'amélioration

Ce document consigne les comportements observés lors d'un test d'interaction entre un agent Codex, le serveur MCPRVTT27 et un projet Revit 2027.

## 1. Création de murs et coordonnées verticales

### Constat

`create_wall` a créé des murs avec un décalage inférieur de 510 mm, alors que l'appel demandait un décalage nul. Le comportement a été observé avec différentes valeurs Z dans `locationLine`.

### Impact

Un mur peut être créé à une altitude inattendue et nécessiter une correction manuelle de `WALL_BASE_OFFSET`.

### Amélioration proposée

- Documenter précisément la convention des coordonnées X/Y/Z : repère projet, repère du niveau ou coordonnées absolues.
- Faire primer explicitement `baseOffset` sur la valeur Z de la ligne, ou retourner le décalage réellement appliqué.
- Ajouter une validation optionnelle indiquant la base, le sommet et les décalages réels après création.

### État après nettoyage du 20 août 2026

Le calcul a été corrigé lorsque `baseLevelId` est fourni : `baseOffset` est
désormais relatif au niveau explicite. La validation géométrique après création
reste à ajouter.

## 2. Portée et persistance de la sélection

### Constat

Les opérations `scope: selection` ne sont pas fiables sur plusieurs appels : une prévisualisation attendue sur 103 pièces n'a initialement traité qu'un seul élément.

### Impact

Une opération de masse peut être appliquée à un périmètre incomplet ou imprévisible.

### Amélioration proposée

- Permettre aux outils bulk d'accepter directement `elementIds`.
- Retourner un identifiant de sélection temporaire réutilisable et expirant explicitement.
- Afficher systématiquement le nombre d'éléments résolus avant l'exécution.

## 3. Résolution des noms de paramètres localisés

### Constat

Dans un projet français, `sync_csv_parameters` n'a pas résolu le paramètre `Numéro` (`matchingParameters: 0`), tandis que `set_element_parameters` a correctement résolu ce nom vers `ROOM_NUMBER`.

### Impact

Le résultat d'une même action dépend de l'outil choisi, ce qui rend l'automatisation fragile dans les environnements non anglophones.

### Amélioration proposée

- Centraliser la résolution des paramètres dans tous les outils.
- Accepter les identifiants `BuiltInParameter` dans les opérations CSV/bulk.
- Retourner un diagnostic clair quand un en-tête CSV ne correspond à aucun paramètre.

## 4. Taille des réponses MCP

### Constat

Certaines réponses de succès détaillent tous les éléments traités (par exemple 103 lignes), même lorsque l'agent a seulement besoin des compteurs, erreurs et de quelques exemples.

### Impact

Les échanges sont plus lents et consomment inutilement le contexte de l'agent.

### Amélioration proposée

- Renvoyer par défaut un résumé : `processed`, `modified`, `skipped`, `errors`.
- Ajouter `includeDetails` et `sampleLimit` pour demander les détails explicitement.
- Prévoir une pagination pour les listes importantes.

## 5. Confirmation et mode automatique

### Constat

La documentation historique associée à certains workflows mentionne des confirmations Revit, tandis que MCPRVTT27 est présenté comme un serveur automatique sans dialogue d'autorisation.

### Impact

Un agent ne sait pas toujours si une action doit être précédée d'un `dryRun`, d'une confirmation utilisateur ou si elle sera appliquée immédiatement.

### Amélioration proposée

- Définir un contrat homogène par outil : lecture, aperçu (`dryRun`), écriture.
- Exposer dans les capacités du serveur le mode effectif : automatique, lecture seule, confirmation Revit requise.
- Consigner l'action, le périmètre et les valeurs avant/après dans un journal d'audit exploitable.

## 6. Ouverture d'un fichier Revit depuis MCP

### Constat

MCPRVTT27 ne propose actuellement pas d'outil permettant d'ouvrir un fichier `.rvt`. Le serveur agit uniquement sur le document déjà ouvert dans Revit.

### Faisabilité API

L'API Revit expose `UIApplication.OpenAndActivateDocument(...)` et
`Application.OpenDocumentFile(...)`, mais leur existence ne suffit pas dans
l'architecture MCP actuelle.

### Contraintes techniques

- L'ouverture ne peut pas avoir lieu pendant une transaction active.
- `OpenAndActivateDocument` ne peut pas être appelée depuis un gestionnaire
  d'événement Revit ; un `ExternalEvent` reste un gestionnaire d'événement API.
- L'implémentation testée via `ExternalEvent` a donc été retirée du fork.
- Une solution sûre demanderait une orchestration distincte (commande Revit
  postée ou pilotage du processus), avec une machine d'état et sans transaction.
- Les modèles collaboratifs et cloud demandent des `OpenOptions` appropriées, notamment pour le détachement du central.

### Amélioration proposée

Ne pas ajouter cet outil au dispatcher actuel. Étudier d'abord une orchestration
compatible avec le cycle de vie Revit, puis seulement exposer un outil explicite :

```text
open_document
- filePath: string
- detachFromCentral?: boolean
- audit?: boolean
```

La réponse devrait contenir le chemin résolu, le titre du document actif, son statut (local, central ou cloud) et les avertissements éventuels.

## 7. Marque et libellés historiques « RevitCortex »

### Constat

Certaines actions et certains titres affichés pendant l'exécution portent encore le nom « RevitCortex », alors que le serveur installé et la configuration active sont MCPRVTT27.

### Impact

Cette incohérence rend le diagnostic ambigu : l'utilisateur ne peut pas identifier avec certitude quel composant a exécuté l'opération ni quel journal consulter.

### Amélioration proposée

- Remplacer les libellés historiques par « MCPRVTT27 » dans les transactions, titres d'action, journaux et messages de retour.
- Exposer la version du serveur et l'identifiant du connecteur dans chaque réponse d'écriture.

### État après nettoyage du 20 août 2026

Les libellés de transactions et de traces actifs ont été renommés
`MCPRVTT27`. Les namespaces C# historiques restent inchangés pour limiter une
migration technique sans bénéfice utilisateur.

## 8. Édition de familles depuis le projet actif

### Constat

Le serveur expose le chargement et l'inventaire des familles, mais aucun outil dédié pour ouvrir une famille du projet actif en mode édition, puis revenir proprement au projet.

### Impact

Un agent peut diagnostiquer les familles chargées, mais ne peut pas tester ni automatiser leur modification dans l'éditeur de familles.

### Amélioration proposée

- Ne pas exposer `edit_family` dans le dispatcher `ExternalEvent` actuel : ce
  scénario modal peut bloquer Revit. Concevoir d'abord une orchestration dédiée.
- Si cette orchestration devient fiable, ajouter `edit_family` /
  `open_family_editor`, avec l'identifiant de famille en entrée.
- Renvoyer un identifiant de session de famille et fournir `save_family`, `load_family_into_project` et `close_family_editor`.
- Refuser explicitement l'opération lorsqu'une transaction projet est active, avec un message actionnable.

## 9. Fiabilité du mode dry-run

### Constat

Un appel `create_level` avec `action: rename` et `dryRun: true` a effectivement renommé un niveau. Le nom a dû être restauré ensuite.

### Impact

Le mode supposé non destructif peut modifier le modèle. C'est un défaut critique pour les opérations de renommage, de niveau et de masse.

### Amélioration proposée

- Garantir que tout `dryRun: true` s'exécute sans transaction d'écriture, ou annule systématiquement la transaction avant retour.
- Ajouter un test automatique de non-régression pour chaque action qui accepte `dryRun`.
- Retourner un champ explicite `mutated: false` dans les réponses d'aperçu.

## 10. Duplication d'étage et décalage de niveaux

### Constat

La création ou le renommage d'un niveau est disponible, mais aucun workflow natif ne permet de dupliquer un étage complet : niveau, éléments de modèle, relations aux niveaux, contraintes hautes et éléments groupés. Le simple décalage d'un niveau a été annulé par Revit en présence de contraintes entre éléments et de groupes de modèles.

### Impact

Une opération métier courante (insérer un étage) exige aujourd'hui des opérations élémentaires risquées et difficiles à orchestrer de façon fiable.

### Amélioration proposée

- Ajouter un outil transactionnel `duplicate_storey` / `insert_storey`.
- Prévoir les options : niveau source, élévation cible, catégories à copier, déplacement des niveaux supérieurs, reprise des contraintes hautes, traitement des groupes et rapport détaillé des exceptions.
- Proposer un aperçu qui liste les éléments copiables, les dépendances bloquantes et les actions de réparation avant écriture.

## 11. Contraintes de murs et groupes de modèles

### Constat

La suppression de la contrainte haute de niveau d'un mur non groupé est possible en fixant `WALL_HEIGHT_TYPE` à non contraint ; 51 murs du R+4 ont été traités avec conservation de leur hauteur. En revanche, la modification des murs membres de groupes de modèles est rejetée hors du mode d'édition du groupe. Les attaches réelles de mur à une dalle, toiture ou plafond ne disposent pas non plus d'un outil MCP dédié.

### Impact

Le détachement préalable nécessaire à un décalage de niveaux ne peut être effectué que partiellement. Les transactions de masse sont annulées dès qu'elles incluent des membres de groupes.

### Amélioration proposée

- Ajouter `detach_wall_constraint` avec un choix explicite : contrainte de niveau, attache haute ou attache basse.
- Ajouter des opérations sûres sur les groupes : inventaire des membres, édition du type de groupe, duplication de type et dissociation contrôlée.
- Retourner, avant écriture, les éléments groupés et les attaches qui nécessitent une stratégie spécifique.

## 12. Gestion des échecs Revit et des avertissements

### Constat

Lors du déplacement des niveaux, Revit a généré des erreurs bloquantes (éléments attachés entre eux, divergences de hauteur dans les groupes) et a annulé la transaction. Le serveur ne distingue pas suffisamment, dans son contrat de réponse, les avertissements supprimables des erreurs qui imposent un rollback.

### Impact

L'agent ne peut pas décider de manière fiable si une transaction peut continuer en consignant des avertissements, ou si une préparation du modèle est obligatoire.

### Amélioration proposée

- Renvoyer une structure normalisée : `warnings`, `errors`, `rolledBack`, `failedElementIds` et `repairHints`.
- Exposer une politique de traitement des avertissements autorisés, sans jamais masquer les erreurs de cohérence qui provoquent l'annulation Revit.
- Prévoir un mode audit qui identifie les éléments à recréer ou réparer après une opération volontairement tolérante.

## 13. Pagination et réponses des recherches d'éléments

### Constat

Une recherche générale sur le R+4 a identifié 674 éléments mais n'en a retourné que 500, avec une réponse très volumineuse.

### Impact

Le résultat est à la fois incomplet et coûteux en contexte pour l'agent, ce qui fragilise les opérations qui doivent établir un inventaire exhaustif.

### Amélioration proposée

- Retourner `totalCount`, `returnedCount`, `nextCursor` et la limite appliquée de manière systématique.
- Ajouter des modes `summary`, `idsOnly` et `details`.
- Permettre des filtres combinés par niveau, catégorie, groupe et statut de contrainte.

## Cas de test recommandés

1. Créer un mur sur plusieurs niveaux avec des valeurs Z et `baseOffset` différentes.
2. Enchaîner sélection, lecture et écriture bulk pour vérifier la stabilité du périmètre.
3. Exécuter les mêmes opérations de paramètres dans des projets FR, EN, IT et DE.
4. Mesurer la taille des réponses pour 10, 100 et 1 000 éléments.
5. Vérifier la cohérence `dryRun` / exécution réelle / journal d'audit.
6. Ouvrir un fichier local, un modèle collaboratif et un modèle cloud, puis vérifier le document actif et la gestion des erreurs.
7. Vérifier que chaque dry-run de création, renommage, déplacement et suppression laisse le document strictement inchangé.
8. Insérer un étage dans un projet contenant des murs contraints, des éléments attachés et des groupes de modèles.
9. Tester les réponses paginées sur des étages contenant plus de 500 éléments.
