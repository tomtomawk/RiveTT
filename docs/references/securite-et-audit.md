# Sécurité et audit

## Le verrou d'écriture est la frontière

`[ToolSafety(readOnly, destructive, supportsDryRun)]` n'est plus de la métadonnée.
Depuis 0.4.0, `readOnly` est une **frontière de permission** : `RiveTTRouter.Route`
refuse tout outil classé `readOnly: false` avec `PermissionDenied` tant que
`WriteAccessPolicy.WritesAllowed` est faux, avant le cache et avant le contrôle de
document ouvert.

Chaque session Revit démarre **verrouillée** (`RiveTTApp.OnStartup` appelle
`WriteAccess.Set(false, "startup")` avant même d'enregistrer les outils). Seul le
bouton *Écriture* du panneau RiveTT du ruban l'ouvre. `WriteAccessGateTests` balaie
tout `RiveTT.Tools` et échoue si un outil appelle `WriteAccessPolicy.Set`.

Conséquences pour ce qu'on ajoute :

- un outil d'écriture marqué `readOnly: true` traverse le verrou. Le trace de
  désaccord entre l'attribut et l'heuristique de préfixe, émis à l'enregistrement,
  ne doit pas être supprimé ;
- `dryRun` n'est pas une exemption : une prévisualisation est une promesse de
  l'outil, le verrou ne peut pas dépendre de 195 implémentations qui la tiennent ;
- le verrou est un état de **session**, pas de document : `RiveTTSession.Reinitialize`
  n'y touche pas.

Ce que le verrou **ne** couvre **pas** : le système de fichiers. Un outil classé
lecture qui écrit un CSV n'est pas arrêté par lui. Voir « Chemins » ci-dessous.

## Le contrat `dryRun`

`supportsDryRun` déclare que l'outil **lit** `dryRun` et rend un aperçu. Quand il est
faux, le routeur **refuse** `dryRun: true` avec `InvalidInput` avant d'exécuter quoi
que ce soit.

Ce n'était pas le cas avant : `EnrichResult` tamponnait `dryRun: true, mutated: false`
sur la seule foi de la demande du client. Les 79 outils d'écriture qui ne lisent pas
`dryRun` s'exécutaient donc normalement, écrivaient dans la maquette, et répondaient
malgré tout que rien n'avait changé. `DryRunGateTests` verrouille le refus et
l'absence d'exécution ; `DryRunDeclarationSourceTests` verrouille la correspondance
déclaration ⇄ comportement dans les deux sens.

`get_server_capabilities` publie la couverture **comptée**
(`dryRun.previewingWriteTools` / `writeTools`), plus jamais un `dryRunDefault: true`
global. Chaque réponse porte `execution.supportsDryRun`.

## Audit

Chaque appel routé est ajouté à `%LOCALAPPDATA%\RiveTT\audit.jsonl` : durée, taille de
réponse, résumé d'entrée et de sortie, nombre d'éléments touchés. Les **refus** y
figurent aussi (verrou fermé, `dryRun` non supporté) : c'est la trace qu'un agent a
tenté d'écrire. `send_code_to_revit` y ajoute l'extrait du code et son SHA-256.

Préserver le filet de `Route` : rien ne doit s'échapper sans être journalisé et
structuré. Une écriture d'audit qui échoue est comptée
(`AuditLogger.WriteFailureCount`) et reportée dans `audit.jsonl.errors.log` — un
`Trace.WriteLine` est invisible dans Revit sans débogueur, et c'est ce silence qui a
laissé le journal cesser de grossir sans que personne le voie.

## Chemins

`PathSafety.TryResolveSafe` résout un chemin fourni par l'appelant et vérifie qu'il
tombe dans une racine autorisée. Il refuse la traversée (`..`), les répertoires
système, et les chemins UNC sauf `allowUnc: true` — réservé aux outils de lien, où
charger une maquette depuis un partage est le geste normal.

`PathSafetySourceTests` vérifie, sur l'ensemble de `RiveTT.Tools`, que tout outil
acceptant un chemin passe par cette porte. La liste est **balayée, pas énumérée** :
une liste `[InlineData]` tenue à la main avait déjà laissé passer six outils.

## `send_code_to_revit`

`CodeSandbox.Validate` s'exécute avant la compilation. Il refuse les accès fichiers et
réseau, la création de processus, le registre, l'interop natif et les détours par la
réflexion.

**Ce n'est pas une frontière de sécurité, et il ne faut pas l'écrire comme si ça
l'était.** C'est un filtre par expressions régulières sur le texte source. Il attrape
l'erreur et le geste évident ; il n'arrête pas quelqu'un qui cherche à passer :

- `System.IO.File.WriteAllText(...)` est du C# valide — la norme autorise
  les échappements `\uXXXX` dans les identifiants — et le motif `System.IO` ne le voit
  pas. Les motifs couvrent désormais cette forme, mais l'espace des réécritures
  équivalentes n'est pas clos par construction ;
- l'API Revit elle-même écrit sur disque (`Document.SaveAs`, les exports) et détruit
  la maquette (`Document.Delete`). Aucune expression régulière ne peut l'interdire
  sans interdire l'outil.

Les vraies frontières sont, dans l'ordre : le verrou d'écriture du ruban, le `dryRun`
par défaut de cet outil, et le journal d'audit horodaté avec le hash du code. Le bac à
sable est la quatrième ligne, pas la première.

## Contrôles

- L'attribut de sûreté correspond au comportement réel, `supportsDryRun` compris.
- Toute exception devient un échec structuré.
- La journalisation d'audit reste best-effort et ne fait jamais tomber l'appel.
- Les tests de durcissement du bac à sable restent actifs.
- Un outil dédié est préféré au code arbitraire.
