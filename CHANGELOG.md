# Changelog

## [1.0.0] - 2026-11-06
Extraction depuis le projet Basket (Night Hooper 92).

### Ajouté
- Package UPM `com.mbolufer.feedback`, assemblies `FeedbackFX.Runtime` / `FeedbackFX.Editor`, namespace `FeedbackFX`.
- Canal **Événements** : `UnityEvent<float> onPlay`, appelé avec le poids.

### Modifié
- Noms d'enum en anglais : `PositionLocale`/`RotationLocale` → `LocalPosition`/`LocalRotation`,
  `StopEtEffacer` → `StopAndClear`, `Reprendre` → `Resume` (sérialisés en entier : données existantes inchangées).
- Valeurs par défaut neutres : `speedParam` et `ShaderFloat.name` vides au lieu de `_BallSpeed`.
- `FindFirstObjectByType` (obsolète) remplacé par `FindAnyObjectByType`.
