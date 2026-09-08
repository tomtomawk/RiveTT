# RiveTT 0.5.4

## Résumé final de l'installateur

- L'installateur vérifie désormais en lecture seule la configuration MCP de Claude
  et ChatGPT lorsque leurs options restent décochées.
- Il distingue explicitement un MCP déjà configuré, une configuration appliquée
  pendant l'installation et une application laissée non configurée.
- Les chemins complets des deux skills sont toujours affichés : le fichier Claude à
  importer manuellement et le `SKILL.md` personnel auto-détecté par ChatGPT.
- Le résumé final utilise une zone défilable afin que les chemins et diagnostics ne
  soient plus coupés par la hauteur limitée du libellé Inno Setup.

## Compatibilité

- Aucun changement du protocole MCP ni des outils Revit.
- Revit 2026.5+ et 2027 restent construits depuis la même base .NET 10.

## Signature temporaire

- L'installateur et son désinstalleur restent signés `Thomas Thébault`.
- Les DLL chargées par Revit restent temporairement non signées : le certificat actuel
  est auto-signé et Revit l'affichait comme une « signature incorrecte » faute de
  chaîne vers une autorité publique approuvée.
- L'exécutable autonome du serveur MCP et le script d'enregistrement restent signés :
  ils ne sont pas chargés par Revit et ne déclenchent donc pas cette alerte.
- La signature de toute la charge utile devra être réactivée avec un certificat de
  signature de code délivré par une autorité reconnue.
