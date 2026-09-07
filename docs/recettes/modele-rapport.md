# Rapport de recette RiveTT — `<run>`

**MODÈLE NON EXÉCUTÉ.** Renseigner dans la conversation ; proposer le nom rapport-`<run>`.md avant de renseigner les résultats.
Un ancien verdict ne constitue jamais une preuve pour cette campagne.

## Contexte

- Début / fin / fuseau :
- Agent / client MCP :
- Version Revit / build / langue / PID :
- Version plugin / serveur / versionMismatch :
- Catalogue observable / outils annoncés dans les pièces / écarts / limites de découverte :
- Skill et protocole reçus dans la conversation :
- Chemins absolus des modèles créés et ouverts :
- Catalogue figé dans le rapport et références des messages de preuve :
- Dossier proposé pour les exports générés par Revit :
- Journal d'audit : inaccessible par défaut / extrait communiqué ou outil dédié :
- État initial/final du verrou :

## Entrées et protection des sources

| Rôle | Source absolue | Copie absolue | Préparation confirmée par l'utilisateur | Ouverture/chargement Revit vérifié | Limites |
|---|---|---|---|---|---|

## Synthèse de campagne

Phase 1 : NON TESTÉ. Phase 2 : NON TESTÉ.
Qualification installateur : hors campagne sauf demande explicite.
Nature des preuves : MCP / observation humaine / non vérifiable.

| Périmètre | Total | PASS | FAIL | BLOQUÉ | NON TESTÉ | INAPPLICABLE | Couverture |
|---|---:|---:|---:|---:|---:|---:|---|
| Outils | | | | | | | |
| Actions/modes | | | | | | | |
| Paramètres publics | | | | | | | |
| Cas de test | | | | | | | |

Couverture exécutée = (PASS + FAIL) / (total - INAPPLICABLE).
Afficher également PASS / (total - INAPPLICABLE). Dénominateur nul : n/a.
Un outil/action/paramètre n'est PASS que si tous ses cas requis sont PASS ;
sinon FAIL prioritaire, puis BLOQUÉ, puis NON TESTÉ. INAPPLICABLE exige que tous
les cas correspondants soient justifiés comme tels. Ne pas compter un refus
attendu comme un nominal réussi. Donner les deux avis si preuves contradictoires.

Conclusion : `<validée / partielle / échec, justification>`.

## Matrice exhaustive

Initialiser une ligne par outil/action observable avant la campagne ; ajouter les
cas et paramètres nécessaires, puis les outils annoncés mais absents. Le tableau
vide ci-dessous doit être rempli, il ne constitue pas une couverture.

| ID cas | Phase/bloc | Outil exact | Action/mode | Paramètres couverts | Cas nominal/limite/erreur | Fixture/IDs | Attendu | Statut | Preuve ou raison |
|---|---|---|---|---|---|---|---|---|---|

## Journal des cas

Pour chaque cas, conserver les arguments JSON exacts et les réponses MCP dans le
rapport/conversation. Reproduire au minimum les champs prouvant le verdict et les
erreurs complètes, référencer le message source et indiquer toute troncature.
Ne pas prétendre enregistrer des JSON sur le PC. Pour les fichiers exportés par
Revit, relever le chemin retourné et le contrôle réalisé via MCP ou par l'utilisateur.
Ne jamais inclure de secrets.

### `<ID>` — `<intitulé>`

- Date/heure/durée :
- Document/PID/vue/verrou :
- Prérequis, IDs/types/hôtes et unités :
- Appel exact et arguments JSON :
- Attendu :
- État avant :
- Aperçu : arguments, réponse, supportsDryRun, mutated, comparaison avant/après :
- Exécution réelle et réponse MCP / message de preuve :
- Lecture indépendante fraîche :
- Contrôle visuel humain et observation :
- Corrélation audit : NON TESTÉ sauf extrait humain ou accès par outil dédié :
- État après, effets secondaires, fichiers :
- Statut et justification :
- Reprise/rollback/jalon absolu si nécessaire :

## Interventions humaines

| Heure | Cas | Geste demandé et motif | Chemin/élément exact | Réponse utilisateur | État contrôlé après reprise |
|---|---|---|---|---|---|

## Défauts

| ID | Gravité | Outil/action/version | Reproduction et arguments | Attendu | Observé | Preuves MCP/humaines | Portée/effets | Suite |
|---|---|---|---|---|---|---|---|---|

## Blocages et non testés

| Cas/outil/action | Statut | Motif vérifié | Fichier ou geste manquant | Prochaine action |
|---|---|---|---|---|

## Synthèse métier de la phase 2

Décrire uniquement les données constatées : niveaux, quantités, pièces/surfaces,
liens, documentation, anomalies et limites. Citer les cas et messages de preuve ;
distinguer données exactes, approximations et informations absentes.

## Livrables et état final

| Rôle | Chemin absolu proposé ou retourné | Preuve MCP ou confirmation humaine | Validation du contenu / limite |
|---|---|---|---|

Modèle final, jalons, exports, protection des sources confirmée humainement, document actif et verrou final :
`<observations>`. Vérifications restantes par version Revit : `<liste>`.
