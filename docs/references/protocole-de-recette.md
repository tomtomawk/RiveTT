# Recette RiveTT — création puis lecture d'un projet existant

Texte de mission à transmettre à l'agent. Ce document prépare une campagne ; il ne
constitue pas un résultat de test. Ne reprendre aucun verdict de la v0.4.

## Mission pour l'agent

Tu reçois uniquement le skill joint et ce protocole. Tu disposes des outils
RiveTT/MCP, mais d'aucun accès direct au disque, au terminal, au dépôt, aux processus
ou aux journaux locaux. Les chemins ci-dessous sont des arguments pour Revit ou des
indications pour l'utilisateur. Ne tente pas d'obtenir un accès aux fichiers avec
send_code_to_revit et ne l'utilise pas pour écrire ton rapport.

Aucune référence externe mentionnée par le skill n'est à charger. Utilise les deux
pièces fournies, les schémas MCP et les capacités retournées ; si une information
indispensable manque, pose une question ciblée. Le modèle de rapport est intégré
à ce protocole. Aucun troisième document n'est nécessaire.

Les copies, créations de dossiers, installations et contrôles de fichiers sont
réalisés par l'utilisateur lorsqu'aucun outil Revit dédié ne les propose. Distingue
preuve MCP, observation humaine rapportée et contrôle impossible. Ne prétends jamais
avoir lu un fichier, calculé son hash ou vérifié son existence à partir de son chemin.

Teste toutes les fonctions de RiveTT installé avec des scénarios métier, des cas
négatifs et des vérifications indépendantes. Commence avec Revit fermé, puis fais
créer un modèle neuf depuis le gabarit fourni. En deuxième phase, lis un projet
existant ouvert par l'utilisateur. Tiens le rapport Markdown dans la conversation
après chaque cas, et fournis une version consolidée à chaque fin de bloc.
Demande les gestes humains nécessaires, attends la réponse et vérifie l'état réel
avant de reprendre. Continue les cas indépendants lorsqu'un prérequis manque.
Ne corrige pas le code pendant la campagne et ne masque pas un défaut par un autre outil.

**Revit fermé est l'état initial, pas un mode de création hors ligne.** Le modèle
sera créé après le lancement de Revit et le chargement du plugin.

## Chemins à utiliser

Les fichiers sources ont été repérés sur disque le 7 septembre 2026. Leur contenu,
leurs liens et leur version Revit restent à constater pendant la campagne.

| Rôle | Chemin absolu |
|---|---|
| Gabarit de création | C:\Users\theba\Desktop\Revit\club_GABARIT 2026.rte |
| Cartouche à charger | C:\Users\theba\Desktop\Revit\CAR_A4_Entête projet.rfa |
| Famille à charger et tester | C:\Users\theba\Desktop\Revit\OUVERTURE_FAMILLE_TEST.rfa |
| Projet existant, phase 2 | C:\Users\theba\Desktop\Revit\Saint-Malo_avenue aristide briand_46.rvt |
| Projet complémentaire si contenu manquant | C:\Users\theba\Desktop\Revit\Appartement_T2_MCP_Test.rvt |

Exclure comme base saine C:\Users\theba\Desktop\Revit\Fichier test 0.4.0.rvt,
déjà modifié par l'ancienne recette, et
C:\Users\theba\Desktop\Revit\RiveTT_TEST_0.2.0.rvt.
C:\Users\theba\Desktop\Revit\Projet1.rvt n'a pas de rôle prévu.
Le skill à utiliser est celui joint à la conversation, quel que soit son nom.
Aucun fichier de documentation local n'est nécessaire.

### Sorties et protection des originaux

Choisir un identifiant unique `<run>` au format AAAA-MM-JJ_HHmmss_Revit2026 ou
AAAA-MM-JJ_HHmmss_Revit2027. Résoudre ces chemins dans le rapport avant tout appel ;
aucun placeholder ne doit subsister dans les arguments exécutés.

| Sortie | Chemin absolu à résoudre |
|---|---|
| Dossier de travail | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\ |
| Modèle créé | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Phase1_Creation.rvt |
| Copie de Saint-Malo | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Phase2_Lecture.rvt |
| Copie complémentaire | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Complement_Appartement.rvt |
| Copies du gabarit, des familles et fichiers auxiliaires | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Entrees\ |
| Sauvegardes avant blocs destructifs | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Jalons\ |
| Exports, catalogue et preuves | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\Sorties\ |
| Rapport | C:\Users\theba\Desktop\Revit\Recettes\`<run>`\rapport-`<run>`.md |

Avant le premier appel, donner à l'utilisateur la liste source → destination avec
chaque chemin entièrement résolu. Lui demander de créer les dossiers et de copier
le gabarit et les deux RFA dans Entrees, ainsi que Saint-Malo vers Phase2_Lecture.rvt.
Demander la copie complémentaire seulement si nécessaire. Attendre sa confirmation ;
noter « préparation confirmée par l'utilisateur », et non « fichiers vérifiés ».
Les ouvertures/chargements Revit suivants vérifient leur utilisabilité.
Ne jamais écraser une sortie préexistante ni enregistrer sur une source.
Les copies ouvertes en 2027 ne servent pas au run 2026.

Créer les jalons avec save_as_document si disponible, puis contrôler le document
actif et revenir au modèle de travail avant de continuer ; sinon demander une copie
à l'utilisateur après sauvegarde.

Le rapport n'est pas écrit sur le PC par l'agent. Fournir son contenu complet selon
le modèle intégré en fin de protocole. Si le client permet une pièce jointe Markdown,
la fournir ; sinon donner un bloc Markdown copiable et demander à l'utilisateur de
l'enregistrer au chemin indiqué. Ne pas annoncer « fichier enregistré » sans preuve
ou confirmation humaine.

Générer les fichiers IFC/CSV/XLSX avec les outils Revit dédiés si possible. Leur
relecture passe par un outil dédié disponible ou un contrôle humain. Pour le
CSV/XLSX aller-retour, demander la modification exacte des cellules et l'enregistrement
par l'utilisateur avant l'import. Pour tout
prérequis absent (RFT, DWG, PDF, image, nuage de points, modèle de coordination,
ressource liée), demander le fichier avec sa destination absolue attendue et
consigner le chemin réellement reçu. Ne pas inventer la présence d'une dépendance.

## Couverture exhaustive à initialiser

1. Découvrir le catalogue MCP exposé au client et appeler get_server_capabilities
   une fois Revit disponible. Consigner les noms/actions/capacités avec date et
   versions dans le rapport. Croiser avec les outils annoncés par le skill joint
   et ce protocole ; noter les absents. Aucun inventaire local ni code source à lire.
   Si le client ne permet pas une découverte complète, signaler « exhaustivité du
   catalogue non vérifiable » et demander à l'utilisateur d'afficher/activer les
   outils disponibles dans son client.
2. Dans le rapport, créer une ligne par outil, puis par action/mode public des outils
   composites, y compris les alias. Dériver les actions des vrais schémas, jamais du
   seul nom. Relever de nouveau les capacités après les changements de contexte.
3. Affecter chaque ligne à un bloc ci-dessous. Toute ligne non affectée devient un
   cas de rattrapage obligatoire en phase 1, ou en phase 2 pour une lecture.
4. Prévoir un cas nominal réel, les limites utiles et une erreur représentative :
   ID absent, type incorrect, entrée ambiguë, chemin inexistant. Tester chaque
   paramètre public avec une valeur non triviale et vérifier son effet ; couvrir
   aussi les interactions importantes et les défauts de paramètres.
5. Un succès vide faute de contenu n'est pas une validation. Créer la fixture en
   phase 1 ou demander le prérequis ; sinon noter BLOQUÉ avec la raison.
   INAPPLICABLE exige une preuve de version/contexte.

Ne pas figer le nombre d'outils d'une ancienne release. La couverture porte sur le
catalogue observable : ne pas prétendre couvrir des outils invisibles et inconnus.
Tout outil annoncé dans les deux pièces mais absent est consigné. Un cas négatif
réussi ne remplace pas un cas nominal.

## Règles d'exécution et de preuve

- Vérifier versions plugin/serveur, versionMismatch, Revit, PID, chemin et titre
  du document actif. Une version discordante suspend la campagne : demander de
  quitter complètement le client IA, réinstaller puis rouvrir ; redémarrer Revit
  si son plugin a changé. Ne pas supposer une version installée.
- Seul l'utilisateur change le verrou au ruban Compléments → RiveTT. Lire
  execution.writesAllowed ; toolReadOnly qualifie l'appel/action. Consulter
  commandsAvailableWhenLocked pour les exemptions, dont la navigation.
- Prévisualiser si supporté. Vérifier mutated:false, puis les éléments, paramètres,
  réglages, fichiers et document actif concernés avant/après. Des comptes identiques
  ne prouvent pas l'absence de modification.
  Pour un effet sur disque non exposé par MCP, demander un contrôle humain ;
  sans contrôle, ce sous-cas reste NON TESTÉ.
- Appliquer avec les mêmes arguments hors dryRun, puis vérifier par une lecture
  indépendante : IDs, géométrie, valeurs et unités. Invalider le cache si nécessaire ;
  une réponse mise en cache ne suffit pas après une écriture.
- Vérifier catégories FR et categoryBic, paramètres FR/EN, unités et valeurs internes,
  noms inconnus signalés, listes, pagination et compteurs. Comparer réponses compactes
  et détaillées quand proposées ; les échecs doivent conserver leurs informations.
- Consigner les durées retournées par l'outil/client ; sinon noter indisponible ou
  approximatif, sans inventer une mesure. Après timeout, relever l'état avant reprise :
  une écriture peut avoir été appliquée. Ne jamais répéter aveuglément une suppression.
- Demander une vérification visuelle humaine des cotes, annotations, feuilles,
  escaliers et géométries non prouvées par les lectures. Consigner son observation ;
  sans elle le cas visuel reste NON TESTÉ.
- send_code_to_revit a un cas dédié : sandbox, aperçu puis script minimal sur le
  modèle jetable. Présenter le code et demander l'accord explicite avant exécution.
  Aucun accès externe ni tentative de lever le verrou. Un refus d'accord est BLOQUÉ.
  Cet outil ne remplace jamais un outil défaillant.
- Mettre à jour le rapport après chaque cas et avant chaque pause. Les demandes
  humaines donnent geste exact, motif, état attendu et contrôle de reprise. Ne pas
  demander une confirmation pour chaque appel déjà autorisé.
  À chaque fin de bloc, joindre un état de reprise : dernier cas terminé, prochain
  cas, document/vue/PID/verrou attendus, IDs des fixtures, chemins et blocages.
  Si la conversation doit être reprise, demander à l'utilisateur de conserver puis
  retransmettre le dernier rapport ; ne pas supposer une mémoire persistante.
- Le journal local n'est pas accessible. Les preuves sont les réponses MCP et les
  lectures Revit indépendantes. La corrélation audit reste NON TESTÉ sauf si un outil
  dédié l'expose ou si l'utilisateur communique volontairement un extrait pertinent.
  Aucun journal supplémentaire n'est requis pour démarrer.

## Phase 1 — Revit fermé, puis création d'un modèle

### P1.0 — démarrage à froid

Demander : « Enregistre ton travail, ferme toutes les instances de Revit et
confirme-moi que Revit est fermé. » Consigner cette confirmation humaine : aucun
accès aux processus n'est disponible. Noter le début de campagne.

Faire un seul diagnostic de connexion : attendre une indisponibilité explicite
dans le délai de transport, jamais un succès inventé. Noter durée et erreur.
Un catalogue MCP accessible ne prouve pas une connexion Revit.

Demander : « Lance Revit 2026.5+ ou 2027 avec RiveTT installé, reste sur l'accueil
sans ouvrir de projet, puis confirme. » Vérifier connexion, versions, verrou fermé
et absence de document. Une lecture nécessitant un document doit rendre une erreur
structurée sans blocage.

### P1.1 — verrou et création depuis le gabarit

Lire la signature MCP courante de create_document. Tester la création vers
Phase1_Creation.rvt depuis la copie du gabarit, verrou fermé, aperçu compris :
attendre PermissionDenied et aucune sortie créée. Demander l'activation d'Écriture,
puis relire le verrou. Prévisualiser et appliquer la création.

Vérifier nouveau projet, chemin enregistré, document effectivement actif et contenu
hérité du gabarit. save_as_document duplique un document, il ne valide pas une
création neuve ; open_template ouvre le gabarit pour édition, ce n'est pas ce test.
Si un document de départ est exigé, consigner l'échec du scénario sans document
avant la reprise humaine ; celle-ci ne valide pas rétroactivement le cas échoué.

### P1.2 — fixtures et parcours métier

Découvrir les IDs/types/hôtes/unités réels. Préfixer les objets d'essai RECETTE_.
Réutiliser ou créer trois niveaux cohérents, par exemple 0/3000/6000 mm, puis un
petit bâtiment d'environ 12 × 8 m avec cinq pièces fermées. Conserver le registre
des IDs et des fichiers. Respecter les unités de chaque signature.

| Bloc | Fonctions et contenu à couvrir | Vérification indépendante |
|---|---|---|
| Architecture/éléments | Niveaux, axes, types système, murs unitaires/en lot, sols, toiture, plafonds, murs-rideaux, ouvertures, composants, escaliers, garde-corps | Hôtes, niveaux, offsets, dimensions, solides, pièces fermées d'aire positive, arrivée d'escalier |
| Familles/matériaux | Charger les deux RFA copiés, découvrir leur catégorie, types/instances, duplication, édition, matériaux, groupes/assemblages | Type réellement chargé, paramètres type/instance, usages et dépendances ; ne pas supposer que la famille fournie est une porte |
| Paramètres/données | Lecture et modification seule/en lot, filtres type/instance, paramètres partagés, CSV/XLSX aller-retour | Valeurs avant/après, unités, champs inconnus signalés, objets JSON acceptés, listes vides traitées explicitement |
| Vues/sélection | Plans, coupes, élévations, 3D, gabarits, duplication, recadrage, affichage, sélection persistée/temporaire, activation | Vue active et sélection exactes, limites et visibilité ; refus d'ID absent, non-vue, gabarit |
| Annotations/feuilles | Cotes par références et points, tags, textes, cinq pièces, deux nomenclatures, trois feuilles avec le cartouche A4 fourni, placement et duplication | Cotes positives exactes et visibles, références attachées, signal hors crop, cartouche présent, vues dans le cadre réel sans superposition |
| Nomenclatures/exports | Champs, filtres, tris, en-têtes simples/groupés, formats d'export/import proposés | Comparaison cellules/modèle, vrais en-têtes distincts des données, nombre et lisibilité des fichiers |
| Projet/diagnostic | Statistiques, avertissements, collisions, phases, options, worksets, réglages, nettoyage, audits | Témoins positifs/négatifs, avertissements identifiés sans dialogue bloquant, compteurs et unités justes |
| Liens/IFC | Petit RVT lié issu d'une sauvegarde d'essai, chargement/rechargement/déchargement, transformations, export IFC puis inspection/import/mapping/propriétés | Hôte/liens distingués, coordonnées et IDs, fichier lisible, caches d'import isolés |
| Documents | RVT/RFA/RTE sur copies, RFT/IFC si disponibles, alias, arrière-plan/activation, sauvegarde/sauvegarde sous/fermeture | Document ciblé après chaque bascule, aperçu sans écriture, extension correcte, refus d'écrasement, persistance après réouverture |
| Workflows/interop | Audit, documentation des pièces, échanges de données et sélection Navisworks selon catalogue | Résultat final vérifié, dépendances absentes explicitement bloquées |
| Destruction/reprise | Actions destructives de chaque outil sur fixtures sacrifiables, à la fin | Cascades prévues/réelles, éléments supprimés, dépendances signalées, absence de résidus après rollback |
| Métadonnées/code | Capacités, ping, cache, classification par action, audit, cas dédié Roslyn | Contrat, fraîcheur, refus sandbox et aperçu sans effet, audit seulement si accessible par outil ou extrait humain |

Pour les fixtures absentes (worksharing, option, coordination, nuage, Navisworks…),
faire une demande humaine précise. Ne pas activer un partage central sur une source.
En 2027 couvrir coordination et mur hébergé sur mur ; en 2026 vérifier la réponse
explicite « non supporté ». Rejouer les cas dépendants de version sur les deux
installations avec deux runs distincts. Une version absente reste NON TESTÉ.

### P1.3 — régressions, verrou et persistance

Les régressions à rejouer sont incluses ci-dessous ; aucun changelog n'est requis.
Une correction annoncée n'est pas une preuve en session réelle. Inclure :

- create_line_based_element : ID de niveau réel, offset relatif et altitude absolue
  distincts ; refus de combinaisons contradictoires et du niveau inconnu.
- Cotes parallèles, verticales et diagonales en plan/coupe : valeur positive,
  visibilité et rollback des références auxiliaires en cas d'échec.
- Annotations hors crop : signalement et visibilité après ajustement.
- add_shared_parameter et outils sans aperçu : dryRun:true refusé sans mutation
  si non supporté. Tester à la couche exposant le paramètre ; un rejet du schéma MCP
  ne prouve pas à lui seul le comportement du routeur.
- RVT déjà ouvert : aperçu de sauvegarde sans faux verrou, sauvegarde réelle puis
  lecture du nouveau chemin après sauvegarde sous.
- activate_view fonctionne verrou fermé, sans script ni clic de substitution :
  relire get_current_view_info. Ne pas appeler l'ancien nom open_view. Tester feuille,
  vue normale, gabarit, ID absent et ID non-vue ; tester une ambiguïté de nom seulement
  si le schéma propose un nom et si une ambiguïté réelle existe.
- Demander les bascules du verrou pour les cas dédiés : mutations refusées,
  actions list/get annoncées et navigation autorisées, action inconnue sans exemption.
- Sauvegarde, réouverture, comparaison des jalons : IDs, valeurs, vues, feuilles,
  avertissements. Tester close_document avec un autre document disponible pour
  basculer, puis son refus sans destination d'activation utilisable.
- Paramètres compacts : toutes les entrées, unités, valeurs internes et valeurs
  vides légitimes conservées ; nom non résolu explicitement signalé.
- Catégories françaises dans les recherches d'éléments non étiquetés et les champs
  de nomenclature : résultats cohérents avec les codes OST_*.
- Nomenclatures à en-têtes groupés : aucun en-tête parasite dans les données,
  aucune vraie ligne supprimée parce qu'elle ressemble à un en-tête.
- Ouverture en arrière-plan sans remplacement du document actif ; ouverture IFC
  et famille issue de RFT sur copies, sauvegarde en RVT/RFA selon le document.
  Sans RFT fourni, noter ce dernier cas BLOQUÉ plutôt que créer un faux fichier.

Terminer les écritures et le rattrapage de la matrice avant la phase 2. Sauvegarder
un jalon avant chaque bloc destructif et conserver un modèle final exploitable.
Un défaut critique de dryRun/verrou suspend les écritures jusqu'à décision humaine ;
seules les lectures indépendantes sûres continuent.

## Phase 2 — lecture d'un projet existant déjà ouvert

1. Faire préparer par l'utilisateur Phase2_Lecture.rvt par copie de Saint-Malo, sans l'ouvrir
   par MCP. Demander : « Ouvre dans Revit `<chemin absolu résolu de Phase2_Lecture.rvt>`,
   mets RiveTT en lecture seule et confirme lorsque le chargement est terminé. »
   Toute conversion reste sur copie. Si version incompatible, demander un fichier
   compatible et consigner le blocage ; ne pas prétendre rétroconvertir.
2. Détecter ce document déjà ouvert, son chemin et son PID. Ne pas le rouvrir ou
   remplacer sous prétexte de se connecter. Vérifier verrou fermé et état initial :
   liens manquants, avertissements, contenu et état modifié du document.
3. Exécuter toutes les lectures/actions de consultation applicables de la matrice :
   projet, niveaux, types/familles, éléments/paramètres, pièces/surfaces/volumes,
   matériaux, phases/options/worksets, liens/coordonnées, IFC, vues/gabarits,
   feuilles/cartouches, nomenclatures, avertissements/collisions. Distinguer hôte/liens.
4. Produire une synthèse métier fondée sur les observations : niveaux, quantités par
   catégorie, pièces/surfaces, organisation documentaire, qualité des données,
   anomalies. Vérifier agrégats par seconde lecture/pagination et échantillons de
   valeurs sources. Signaler toute approximation géométrique.
5. Tester navigation et sélection autorisées verrou fermé. Faire changer une vue
   manuellement, puis vérifier que l'agent détecte ce changement sans donnée périmée.
   Aucune écriture réelle du modèle, suppression, rechargement de lien ou import :
   vérifier l'effet de chaque action, pas seulement la classification globale de
   l'outil composite. Les tests de refus de mutation ont déjà eu lieu en phase 1.
6. Exporter les lectures via Revit dans Sorties ; faire contrôler les fichiers par
   un outil dédié ou l'utilisateur. Écrire un export
   n'autorise pas à modifier le modèle. Si contenu manquant, proposer la copie
   Complement_Appartement.rvt et documenter la bascule humaine.
7. Comparer les états ciblés et l'état modifié du document via Revit avant/après.
   Ne pas sauvegarder pour masquer une mutation. Distinguer ces observations d'une
   preuve d'intégrité disque inaccessible. Toute mutation inattendue est un défaut.

## Clôture et critères d'acceptation

Calculer séparément couverture des outils, actions et paramètres, avec dénominateurs
issus du catalogue figé et écarts expliqués. Zéro ligne sans statut. PASS exige
effet vérifié, pas simplement success:true. Un refus attendu valide seulement son cas.

La campagne est terminée quand toutes les lignes sont qualifiées et les deux phases
documentées. Elle n'est intégralement validée que si tous les cas applicables passent,
les preuves existent, la protection des sources est documentée et aucun défaut critique/majeur
n'est ouvert. BLOQUÉ/NON TESTÉ empêchent d'annoncer « toutes les fonctions testées ».

Demander le retour du verrou en lecture seule et le contrôler. Faire confirmer à
l'utilisateur qu'aucun original n'a été remplacé ; sans accès disque, ne pas annoncer
une intégrité vérifiée par hash. Produire le rapport consolidé, son nom de fichier et
son chemin d'enregistrement proposé, le chemin du modèle créé, les compteurs et les
gestes restants. Faire enregistrer le rapport par l'utilisateur si aucun téléchargement
Markdown n'est disponible. Garder dans la conversation les preuves et états de reprise.
Aucune modification de changelog, de code ou de PR n'est demandée à cet agent.

## Annexe — installateur, pour qualification de release

Bloc optionnel, uniquement sur demande de qualification de release, séparé des deux
phases métier. Planifier une session dédiée s'il faut fermer
le client IA courant ; ne pas déclencher de mise à jour/désinstallation au milieu
de la recette. Demander à l'utilisateur de choisir le vrai installateur dans
C:\Users\theba\Projets code\RiveTT\dist\ et de communiquer son chemin complet et
sa version. L'agent ne parcourt pas ce dossier et ne calcule pas de hash.
Aucun nom/version d'exécutable ne doit être inventé.

| Cas | Situation à faire réaliser par l'utilisateur | Attendu |
|---|---|---|
| I.1 | Mise à jour depuis une ancienne version, client MCP ouvert | Avertissement d'utilisation |
| I.2 | Refus de poursuivre | Arrêt, versions inchangées |
| I.3 | Poursuite avec ancien serveur effectivement verrouillé | Mise à jour incomplète signalée, pas de faux succès |
| I.4 | Client complètement quitté, installation puis relance | Versions cohérentes |
| I.5 | Mise à jour avec Revit ouvert | Ancien plugin en mémoire, nouveau après redémarrage |
| I.6 | Désinstallation avec Revit ouvert | Refus |
| I.7 | Capacités après redémarrage | Versions cohérentes, pas de versionMismatch |
| I.8 | Installation depuis Explorateur sans admin puis contexte empaqueté | Aucune UAC ; contexte empaqueté refusé suivant contrat courant |

Sans ancien serveur verrouillé, I.3 reste NON TESTÉ. Relever les copies virtualisées
signalées, sans les effacer automatiquement.


## Modèle de rapport intégré — à produire dans la conversation

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
