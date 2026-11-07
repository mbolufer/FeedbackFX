# Feedback FX : restitution générique (game feel) pour Unity 6

Un composant **Feedback** regroupe tout ce qu'un événement de jeu doit déclencher (son, particules,
timeline, shake…). Le script de gameplay ne connaît que le Feedback et l'appelle :

```csharp
using FeedbackFX;

public Feedback hitFeedback;

void OnHit(float impactSpeed) => hitFeedback.Play(impactSpeed); // pondéré par la vitesse
void OnScore()                => scoreFeedback.Play();           // poids = 1
```

Prérequis : Unity 6. Fonctionne avec tous les pipelines (Built-in, URP, HDRP) : le canal **Post Process**
n'apparaît que si `com.unity.render-pipelines.core` est installé (toujours le cas en URP / HDRP) — il est
alors compilé grâce au define `FEEDBACKFX_SRP`.

```
https://github.com/mbolufer/FeedbackFX.git#v1.1.0
```

```
com.mbolufer.feedback/
├── Runtime/Feedback.cs         ← le composant
├── Runtime/ShaderManager.cs    ← globales shader (Color, float, Vector3) éditables, ExecuteAlways
└── Editor/                     ← inspecteur par catégories, previews hors Play, drawers une ligne
```

Namespace : `FeedbackFX`. Assemblies : `FeedbackFX.Runtime`, `FeedbackFX.Editor`.

---

## Pondération

`Play(float impactSpeed)` normalise la vitesse entre **Min Speed** et **Max Speed**, puis la passe dans
**Response Curve** pour obtenir un **poids** (peut dépasser 1). `Play()` utilise un poids de 1.
Le poids module le volume, le pitch, la vitesse de l'Animator, l'amplitude du pulse, le shake et le weight du post-process.

## Canaux

Chaque canal s'active par sa case dans l'en-tête de sa catégorie (vert = actif, orange = actif mais incomplet).

| Canal | Ce qu'il fait | Pondéré |
|---|---|---|
| Activation d'objets | `SetActive` sur une liste d'objets, appliqué avant les autres canaux | non |
| Animator | Trigger, float de poids, vitesse de l'Animator | oui |
| Son | `PlayOneShot` avec jitter de pitch | oui |
| FX (ParticleSystem) | Play / Stop / StopAndClear par entrée | non |
| Timeline | Play / Pause / Resume / Stop d'un PlayableDirector | non |
| Événements | `UnityEvent<float>` appelé avec le poids : pour tout le reste (glitch VHS, vibration, script du jeu) | oui |
| Pulse sinusoïdal | Oscillation amortie du scale, de la position ou de la rotation locale | oui |
| Shader global | Lerp de floats globaux vers une valeur cible (passe par le ShaderManager s'il gère la propriété) | non |
| Post Process *(URP / HDRP)* | Weight d'un Volume 0 → poids → 0 | oui |
| Camera Shake | Bruit Perlin sur la position locale de la caméra | oui |

Les valeurs de repos (transform, caméra, weight) sont restaurées à la fin, sur `Stop()` et à la désactivation.

## API

| Membre | Rôle |
|---|---|
| `Play()` / `Play(float impactSpeed)` | Déclenche tous les canaux actifs |
| `Stop()` | Interrompt timelines et coroutines, restaure les valeurs de repos |
| `Duration` | Durée du canal le plus long : pour attendre la fin d'un feedback dans une séquence |
| `Weight(float impactSpeed)` | Poids calculé pour une vitesse |

## Éditeur

- **▶ Tester** (Play uniquement) joue le feedback à vitesse max.
- **▶ Preview pulse** et **▶ Preview shake** fonctionnent aussi hors Play ; les valeurs sont restaurées à la fin.

## ShaderManager

Liste de globales shader appliquées par `Shader.SetGlobal*` en Play et en éditeur. Un vecteur peut suivre la position
d'un GameObject. `SetFloat` / `SetColor` / `SetVector` modifient l'entrée et l'appliquent tout de suite : sans ça,
l'`Update()` du manager réécrirait la valeur chaque frame.

## Notes

- Le camera shake écrit directement `localPosition` : il entre en conflit avec un autre script qui déplace la même caméra.
  Le poser sur un parent ou un enfant dédié de la caméra.
- Un même Feedback peut servir à plusieurs déclencheurs ; dans ce cas, renseigner des cibles explicites
  (`sineTarget`, `audioSource`…) plutôt que de compter sur le transform du composant.
