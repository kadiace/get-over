# GetOver

GetOver is a simple 3D arcade game where you become a chicken that has fallen into a circular waterfall and must escape the hole. You can play game [HERE](https://kadiace.github.io/get-over/)

## Controls

- `↑ ↓ ← →`: Move
- `Space`: Jump
- `Enter`: Restart Game

## Key Runtime Scripts

- `Assets/Scripts/Scenes/GameScene.cs`
  - scene bootstrap
  - floor spawn loop
  - score update and retry handling
- `Assets/Scripts/Controllers/PlayerController.cs`
  - movement, jump, ground checks, and animation state
- `Assets/Scripts/Controllers/FloorController.cs`
  - floor movement/despawn and darkness transition
- `Assets/Scripts/Controllers/BubbleEmitterController.cs`
  - pooled bubble spawn, growth, pop, and area shaping

## Notes

- This project follows a manager-oriented architecture (`Managers`, `@Manager` pattern).
- `PoolManager` is used to efficiently manage repeatedly spawned and despawned resources such as Floor objects and Bubble effects.
