GolfHole model

Files:
- GolfHole.obj: beveled cup rim, inset liner, dark cup floor, striped pole, and a single clean coral pennant.
- GolfHole.mtl: matching warm gold, mint, deep plum, coral, and cream materials.

Unity setup:
1. Keep the OBJ and MTL in the same folder so the model can load its materials.
2. Place it with the ground surface at Y = 0. The cup opening is about 1.48 Unity units wide; the flag reaches about 2.5 units above ground.
3. Enable Generate Colliders in the OBJ Model Import Settings for physical cup/rim collision.
4. Keep a separate HoleTrigger collider over the opening for scoring and course progression.
5. If your ball is larger or smaller than 1 unit, scale the model until the opening is wider than the ball.
