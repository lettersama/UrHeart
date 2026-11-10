using UnityEngine;

public sealed partial class OSCursorExperimentController
{
    // Positive native Y points down. The ball is cosmetic and never applies a
    // force to the pointer or participates in the player's failure conditions.
    const float TetherLength = 12f;
    const float TetherGravity = 220f;
    const float TetherAirDrag = 1.8f;
    const float TetherFloorFriction = 180f;
    const float TetherContactGap = .015f;
    static readonly Vector2 TetherAttachment = new Vector2(2f, 2f);
    enum TetherSpace { Free, Cage, Maze }
    Vector2 tetherAnchor, tetherBall, tetherVelocity, tetherRopeBend;
    bool tetherReady, tetherGrounded;
    TetherSpace tetherSpace;
    int tetherCornerOverrides, tetherUnresolvedContacts;
    readonly System.Collections.Generic.List<Rect> tetherNearbyWalls = new System.Collections.Generic.List<Rect>(16);

    float TetherBallRadius => smallWeight != null
        ? Mathf.Max(smallWeight.rect.width, smallWeight.rect.height) * .5f : 5f;

    void ResetTether()
    {
        tetherReady = false;
        tetherGrounded = false;
        tetherVelocity = Vector2.zero;
        tetherCornerOverrides = 0;
        tetherUnresolvedContacts = 0;
    }

    bool TryGetTetherAnchor(out Vector2 anchor, out TetherSpace space)
    {
        anchor = Vector2.zero;
        space = TetherSpace.Free;
        if (round != 1) return false;
        if (firstTestTransitionActive)
        {
            anchor = FirstTestTransferPointerPosition() + TetherAttachment;
            if (firstTestTransitionTime <= FirstTestDoorSeconds) space = TetherSpace.Cage;
            return true;
        }
        if ((phase == ExperimentPhase.Intake || phase == ExperimentPhase.Recapture)
            && showCageStoryWeight && IsPointerFreeInCage)
        {
            anchor = cagePointer + TetherAttachment;
            space = TetherSpace.Cage;
            return true;
        }
        if (phase == ExperimentPhase.RoundIntro || phase == ExperimentPhase.Playing || phase == ExperimentPhase.Collision)
        {
            anchor = MazePointerTopLeft(pointer) + TetherAttachment;
            space = TetherSpace.Maze;
            return true;
        }
        return false;
    }

    void EnsureTether(Vector2 anchor, TetherSpace space)
    {
        if (tetherReady) return;
        tetherAnchor = anchor;
        tetherSpace = space;
        tetherBall = ResolveTetherPenetration(anchor + new Vector2(-4f, 6f), space);
        tetherBall = anchor + Vector2.ClampMagnitude(tetherBall - anchor, TetherLength);
        tetherVelocity = Vector2.zero;
        tetherReady = true;
        RefreshTetherRope();
    }

    void TickTether(float dt)
    {
        Vector2 anchor;
        TetherSpace space;
        if (!TryGetTetherAnchor(out anchor, out space)) { ResetTether(); return; }
        EnsureTether(anchor, space);
        if (dt <= 0f) return;
        if (space != tetherSpace)
        {
            tetherBall = ResolveTetherPenetration(tetherBall, space);
            tetherSpace = space;
        }
        Vector2 previousAnchor = tetherAnchor;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector2 pin = Vector2.Lerp(previousAnchor, anchor, (i + 1f) / steps);
            StepTetherBody(pin, space, step);
        }
        tetherAnchor = anchor;
        RefreshTetherRope();
    }

    void StepTetherBody(Vector2 pin, TetherSpace space, float dt)
    {
        Vector2 before = tetherBall;
        // Ground contact eats the tiny repeated gravity impulse. Static friction
        // is exact: a resting ball does not drift when a slack pin moves nearby.
        Vector2 probe = MoveTetherBody(tetherBall, new Vector2(0f, .08f), space);
        bool supported = probe.y - tetherBall.y < .04f;
        if (supported && tetherVelocity.y >= 0f)
        {
            tetherVelocity.y = 0f;
            tetherVelocity.x = Mathf.MoveTowards(tetherVelocity.x, 0f, TetherFloorFriction * dt);
        }
        else tetherVelocity *= Mathf.Exp(-TetherAirDrag * dt);
        tetherVelocity.y += TetherGravity * dt;
        Vector2 motion = tetherVelocity * dt;
        Vector2 moved = MoveTetherBody(tetherBall, motion, space);
        if (Mathf.Abs(moved.x - tetherBall.x - motion.x) > .0001f) tetherVelocity.x = 0f;
        if (Mathf.Abs(moved.y - tetherBall.y - motion.y) > .0001f)
        {
            // Ignore resting gravity corrections; only a real falling weight
            // gets a small metal contact, below the pointer's failure sound.
            if (!tetherGrounded && tetherVelocity.y > 28f)
                Chapter02Audio.Play("Chapter02_TetherAttach", .3f, .82f, .22f);
            tetherVelocity.y = 0f;
        }
        tetherBall = moved;

        Vector2 separation = tetherBall - pin;
        float distance = separation.magnitude;
        if (distance > TetherLength)
        {
            // A chain only pulls after it becomes taut; it never pushes its ball
            // outward just to maintain an endpoint distance equal to its length.
            Vector2 target = pin + separation * (TetherLength / distance);
            Vector2 pulled = MoveTetherBody(tetherBall, target - tetherBall, space);
            if (Vector2.Distance(pin, pulled) > TetherLength + .001f)
            {
                // Only this uncommon wall/tension conflict needs a safe landing.
                // Solve the reachable free region once, not a new angle each frame.
                Vector2 safe;
                if (TryFindTetherSafeLanding(pin, target, space, out safe)) pulled = safe;
                else
                {
                    // An edited layout can have no ball-sized space in reach.
                    // Keep the last nonpenetrating position instead of embedding
                    // the body in a wall; the approved layouts verify this is unused.
                    pulled = tetherBall;
                    tetherUnresolvedContacts++;
                }
                tetherCornerOverrides++;
            }
            tetherBall = pulled;
            tetherVelocity = Vector2.ClampMagnitude((tetherBall - before) / dt, 140f);
        }
        // A restored/transitioned position can precede this contact convention.
        // Repair only an invalid landing, never repeatedly project a resting body.
        if (!TetherBallClear(tetherBall, space))
        {
            Vector2 safe;
            if (TryFindTetherSafeLanding(pin, tetherBall, space, out safe))
            {
                tetherBall = safe;
                tetherVelocity = Vector2.zero;
                tetherCornerOverrides++;
            }
            else tetherUnresolvedContacts++;
        }
        tetherGrounded = MoveTetherBody(tetherBall, new Vector2(0f, .08f), space).y - tetherBall.y < .04f;
        if (tetherGrounded && tetherVelocity.y > 0f) tetherVelocity.y = 0f;
        if (tetherGrounded && Mathf.Abs(tetherVelocity.x) < .08f) tetherVelocity.x = 0f;
    }

    bool TryFindTetherSafeLanding(Vector2 pin, Vector2 preferred, TetherSpace space, out Vector2 landing)
    {
        landing = preferred;
        float best = float.PositiveInfinity;
        ConsiderTetherLanding(preferred, pin, preferred, space, ref landing, ref best);
        ConsiderTetherLanding(ResolveTetherPenetration(preferred, space), pin, preferred, space, ref landing, ref best);
        ConsiderTetherLanding(ResolveTetherPenetration(pin, space), pin, preferred, space, ref landing, ref best);
        tetherNearbyWalls.Clear();
        if (space == TetherSpace.Maze && layout != null)
        {
            float extent = TetherBallRadius + TetherContactGap;
            foreach (Rect source in layout.Walls)
            {
                Rect wall = new Rect(source.xMin + MazeOffset.x - extent, source.yMin + MazeOffset.y - extent,
                    source.width + extent * 2f, source.height + extent * 2f);
                if (wall.xMax < pin.x - TetherLength || wall.xMin > pin.x + TetherLength
                    || wall.yMax < pin.y - TetherLength || wall.yMin > pin.y + TetherLength) continue;
                tetherNearbyWalls.Add(wall);
            }
        }
        // The closest point of disk-minus-rectangles lies on an edge projection,
        // an edge/disk intersection, or a pair of intersecting wall edges.
        foreach (Rect wall in tetherNearbyWalls)
        {
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? wall.xMin : wall.xMax;
                float y = side == 0 ? wall.yMin : wall.yMax;
                ConsiderTetherLanding(new Vector2(x, Mathf.Clamp(preferred.y, wall.yMin, wall.yMax)), pin, preferred, space, ref landing, ref best);
                ConsiderTetherLanding(new Vector2(Mathf.Clamp(preferred.x, wall.xMin, wall.xMax), y), pin, preferred, space, ref landing, ref best);
                float vertical = TetherLength * TetherLength - (x - pin.x) * (x - pin.x);
                float horizontal = TetherLength * TetherLength - (y - pin.y) * (y - pin.y);
                if (vertical >= 0f)
                {
                    float d = Mathf.Sqrt(vertical);
                    ConsiderTetherLanding(new Vector2(x, pin.y - d), pin, preferred, space, ref landing, ref best);
                    ConsiderTetherLanding(new Vector2(x, pin.y + d), pin, preferred, space, ref landing, ref best);
                }
                if (horizontal >= 0f)
                {
                    float d = Mathf.Sqrt(horizontal);
                    ConsiderTetherLanding(new Vector2(pin.x - d, y), pin, preferred, space, ref landing, ref best);
                    ConsiderTetherLanding(new Vector2(pin.x + d, y), pin, preferred, space, ref landing, ref best);
                }
                foreach (Rect other in tetherNearbyWalls)
                {
                    ConsiderTetherLanding(new Vector2(x, other.yMin), pin, preferred, space, ref landing, ref best);
                    ConsiderTetherLanding(new Vector2(x, other.yMax), pin, preferred, space, ref landing, ref best);
                }
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    void ConsiderTetherLanding(Vector2 candidate, Vector2 pin, Vector2 preferred, TetherSpace space, ref Vector2 landing, ref float best)
    {
        if (Vector2.Distance(candidate, pin) > TetherLength + .0001f || !TetherBallClear(candidate, space)) return;
        float score = (candidate - preferred).sqrMagnitude;
        if (score >= best) return;
        landing = candidate;
        best = score;
    }

    bool TetherBallClear(Vector2 center, TetherSpace space)
    {
        float radius = TetherBallRadius;
        if (space == TetherSpace.Free) return true;
        if (space == TetherSpace.Cage)
            return center.x >= CageInterior.xMin + radius && center.x <= CageInterior.xMax - radius
                && center.y >= CageInterior.yMin + radius && center.y <= CageInterior.yMax - radius;
        if (layout == null) return true;
        radius += TetherContactGap;
        foreach (Rect wall in layout.Walls)
        {
            Vector2 point = center - MazeOffset;
            // Same expanded rectangle as the sweep, with only floating point
            // tolerance. Otherwise a valid landing can sit beyond its sweep face.
            if (point.x > wall.xMin - radius + .0001f && point.x < wall.xMax + radius - .0001f
                && point.y > wall.yMin - radius + .0001f && point.y < wall.yMax + radius - .0001f) return false;
        }
        return true;
    }

    Vector2 MoveTetherBody(Vector2 position, Vector2 movement, TetherSpace space)
    {
        if (space == TetherSpace.Free) return position + movement;
        Vector2 result = position;
        result.x = SweepTetherAxis(position, movement.x, true, space);
        result.y = SweepTetherAxis(result, movement.y, false, space);
        return result;
    }

    float SweepTetherAxis(Vector2 start, float movement, bool horizontal, TetherSpace space)
    {
        float origin = horizontal ? start.x : start.y;
        float target = origin + movement;
        if (Mathf.Abs(movement) < .000001f) return origin;
        float radius = TetherBallRadius;
        if (space == TetherSpace.Cage)
        {
            return Mathf.Clamp(target, (horizontal ? CageInterior.xMin : CageInterior.yMin) + radius,
                (horizontal ? CageInterior.xMax : CageInterior.yMax) - radius);
        }
        if (layout == null) return target;
        for (int i = 0; i < layout.Walls.Length; i++)
        {
            Rect wall = layout.Walls[i];
            float near = (horizontal ? wall.xMin + MazeOffset.x : wall.yMin + MazeOffset.y) - radius - TetherContactGap;
            float far = (horizontal ? wall.xMax + MazeOffset.x : wall.yMax + MazeOffset.y) + radius + TetherContactGap;
            float lateral = horizontal ? start.y : start.x;
            float low = (horizontal ? wall.yMin + MazeOffset.y : wall.xMin + MazeOffset.x) - radius - TetherContactGap;
            float high = (horizontal ? wall.yMax + MazeOffset.y : wall.xMax + MazeOffset.x) + radius + TetherContactGap;
            if (lateral <= low || lateral >= high) continue;
            if (movement > 0f && origin <= near + .0001f && target > near) target = Mathf.Min(target, near);
            if (movement < 0f && origin >= far - .0001f && target < far) target = Mathf.Max(target, far);
        }
        return target;
    }

    Vector2 ResolveTetherPenetration(Vector2 position, TetherSpace space)
    {
        float radius = TetherBallRadius;
        if (space == TetherSpace.Cage)
            return new Vector2(Mathf.Clamp(position.x, CageInterior.xMin + radius, CageInterior.xMax - radius),
                Mathf.Clamp(position.y, CageInterior.yMin + radius, CageInterior.yMax - radius));
        if (space != TetherSpace.Maze || layout == null) return position;
        // Only initialization and scene transfer need penetration repair. Doing
        // this every frame fights resting contacts and creates visible shaking.
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            foreach (Rect source in layout.Walls)
            {
                Rect wall = new Rect(source.xMin + MazeOffset.x - radius - TetherContactGap,
                    source.yMin + MazeOffset.y - radius - TetherContactGap,
                    source.width + 2f * (radius + TetherContactGap), source.height + 2f * (radius + TetherContactGap));
                if (!wall.Contains(position)) continue;
                float left = position.x - wall.xMin, right = wall.xMax - position.x;
                float top = position.y - wall.yMin, bottom = wall.yMax - position.y;
                float least = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));
                if (least == top) position.y = wall.yMin;
                else if (least == left) position.x = wall.xMin;
                else if (least == right) position.x = wall.xMax;
                else position.y = wall.yMax;
                changed = true;
            }
            if (!changed) break;
        }
        return position;
    }

    void RefreshTetherRope()
    {
        weightPoint = tetherBall - MazeOffset - TetherAttachment;
        tetherRopeBend = FindTetherRopeBend(tetherAnchor, tetherBall, tetherSpace);
    }

    Vector2 FindTetherRopeBend(Vector2 from, Vector2 to, TetherSpace space)
    {
        Vector2 middle = (from + to) * .5f;
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance >= TetherLength - .0001f) return middle;
        Vector2 axis = distance > .0001f ? delta / distance : Vector2.right;
        Vector2 perpendicular = new Vector2(-axis.y, axis.x);
        float major = TetherLength * .5f;
        float minor = Mathf.Sqrt(Mathf.Max(0f, TetherLength * TetherLength - distance * distance)) * .5f;
        // Points on this ellipse have |from-bend|+|bend-to| == chain length.
        // Select the lowest resting fold that stays clear of the floor/walls.
        // This folds spare links; it never changes the ball's physical position.
        Vector2 best = middle;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < 48; i++)
        {
            float angle = i * (Mathf.PI * 2f / 48f);
            Vector2 candidate = middle + axis * (major * Mathf.Cos(angle)) + perpendicular * (minor * Mathf.Sin(angle));
            bool clear = TetherRopePointClear(candidate, space);
            for (int j = 1; j <= 3 && clear; j++)
                clear = TetherRopePointClear(Vector2.Lerp(from, candidate, j / 4f), space)
                    && TetherRopePointClear(Vector2.Lerp(to, candidate, j / 4f), space);
            float score = candidate.y - (clear ? 0f : 1000f);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    bool TetherRopePointClear(Vector2 point, TetherSpace space)
    {
        if (space == TetherSpace.Cage) return CageInterior.Contains(point);
        if (space != TetherSpace.Maze || layout == null) return true;
        foreach (Rect wall in layout.Walls) if (wall.Contains(point - MazeOffset)) return false;
        return true;
    }

    Vector2 TetherBallForPresentation(Vector2 cursorPosition)
    {
        Vector2 anchor;
        TetherSpace space;
        if (TryGetTetherAnchor(out anchor, out space)) EnsureTether(anchor, space);
        // Rendering has no general cursor delta compensation. Only explicit
        // collision recoil translates the entire displayed pair together.
        Vector2 recoil = phase == ExperimentPhase.Collision ? cursorPosition - MazePointerTopLeft(pointer) : Vector2.zero;
        return tetherBall + recoil;
    }

    Vector2 TetherRopePoint(float distance, Vector2 displayedAnchor, Vector2 displayedBall)
    {
        Vector2 recoil = phase == ExperimentPhase.Collision ? displayedAnchor - tetherAnchor : Vector2.zero;
        Vector2 bend = tetherRopeBend + recoil;
        float first = Vector2.Distance(displayedAnchor, bend);
        if (distance <= first && first > .0001f) return Vector2.Lerp(displayedAnchor, bend, distance / first);
        float second = Vector2.Distance(bend, displayedBall);
        return second > .0001f ? Vector2.Lerp(bend, displayedBall, (distance - first) / second) : displayedBall;
    }

#if UNITY_EDITOR
    public bool DebugTetherActive => tetherReady;
    public bool DebugTetherGrounded => tetherGrounded;
    public Vector2 DebugTetherAnchor => tetherAnchor;
    public Vector2 DebugTetherBall => tetherBall;
    public Vector2 DebugTetherVelocity => tetherVelocity;
    public float DebugTetherLength => Vector2.Distance(tetherAnchor, tetherBall);
    public float DebugTetherRenderedLength => Vector2.Distance(tetherAnchor, tetherRopeBend) + Vector2.Distance(tetherRopeBend, tetherBall);
    public float DebugTetherFixedLength => TetherLength;
    public int DebugTetherCornerOverrides => tetherCornerOverrides;
    public int DebugTetherUnresolvedContacts => tetherUnresolvedContacts;
    public bool DebugTetherBallClear => TetherBallClear(tetherBall, tetherSpace);
    public void DebugSeedTether(Vector2 nativeBallPosition, Vector2 velocity)
    {
        tetherBall = nativeBallPosition;
        tetherVelocity = velocity;
        tetherReady = true;
        RefreshTetherRope();
        RenderPresentation();
    }
#endif
}
