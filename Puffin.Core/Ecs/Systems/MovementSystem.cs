using System;
using System.Collections.Generic;
using Puffin.Core.Ecs.Components;

namespace Puffin.Core.Ecs.Systems
{
    class MovementSystem : ISystem
    {
        private IList<Entity> entities = new List<Entity>();
        private IList<Entity> collidables = new List<Entity>();

        public void OnAddEntity(Entity entity)
        {
            if (entity.GetIfHas<FourWayMovementComponent>() != null)
            {
                this.entities.Add(entity);
            }

            if (entity.GetIfHas<CollisionComponent>() != null)
            {
                this.collidables.Add(entity);
            }
        }

        public void OnRemoveEntity(Entity entity)
        {
            this.entities.Remove(entity);
        }

        public void OnUpdate(TimeSpan elapsed)
        {
            // Separate out updating keyboard (intention to move) with collision resolution).
            // For cases where movement from one tile collides/resolves into another tile (eg.
            // standing in the top-left wall corner of the map, pressing up and left together,
            // top-tile collides/resolves into the left tile). Splitting this into two rounds
            // of resolution fixes this.
            //
            // We also want to just update the intention to move once, because the first round
            // of resolution modifies it to non-collide.
            var halfElapsed = TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / 2);
            foreach (var entity in this.entities)
            {
                // Apply keyboard/intended movement
                entity.GetIfHas<FourWayMovementComponent>()?.OnUpdate(elapsed);

                // Resolve collisions twice to stabilize multi-collisions.
                this.ProcessMovement(halfElapsed, entity);
                this.ProcessMovement(halfElapsed, entity);
            }

            foreach (var entity in this.entities)
            {
                var movementComponent = entity.GetIfHas<FourWayMovementComponent>();
                if (movementComponent != null)
                {
                    entity.X += movementComponent.IntendedMoveDeltaX;
                    entity.Y += movementComponent.IntendedMoveDeltaY;
                    movementComponent.IntendedMoveDeltaX = 0;
                    movementComponent.IntendedMoveDeltaY = 0;
                }
            }
        }

        private void ProcessMovement(TimeSpan elapsed, Entity entity)
        {
            var movementComponent = entity.GetIfHas<FourWayMovementComponent>();
            if (movementComponent.IntendedMoveDeltaX != 0 || movementComponent.IntendedMoveDeltaY != 0)
            {
                // If the entity has a collision component, we have to apply collision resolution.
                if (entity.GetIfHas<CollisionComponent>() != null)
                {
                    var entityCollision = entity.GetIfHas<CollisionComponent>();
                    // See if the entity collided with any solid tiles.
                    var tileMaps = Scene.LatestInstance.TileMaps;

                    foreach (var tileMap in tileMaps)
                    {
                        int targetTileX = (int)Math.Floor((entity.X + movementComponent.IntendedMoveDeltaX) / tileMap.TileWidth);
                        int targetTileY = (int)(Math.Floor(entity.Y + movementComponent.IntendedMoveDeltaY) / tileMap.TileHeight);
                        var targetTile = tileMap.Get(targetTileX, targetTileY);

                        if (targetTile != null && tileMap.GetDefinition(targetTile).IsSolid)
                        {
                            var collideAgainst = new Entity()
                                .Move(targetTileX * tileMap.TileWidth, targetTileY * tileMap.TileHeight)
                                .Collide(tileMap.TileWidth, tileMap.TileHeight);

                            resolveAabbCollision(entity, collideAgainst, elapsed.TotalSeconds);
                        }
                    }

                    // Compare against collidable entities
                    foreach (var collidable in this.collidables)
                    {
                        if (collidable != entity && collidable.GetIfHas<CollisionComponent>() != null)
                        {
                            var collideAgainstComponent = collidable.GetIfHas<CollisionComponent>();
                            resolveAabbCollision(entity, collidable, elapsed.TotalSeconds);
                        }
                    }
                }
            }
        }

        // Checks for AABB collisions between entity (moving) and collideAgainst (hopefully not moving).
        // The output is to modify the IntendedMoveX/IntendedMoveY on entity so that it will be just at the point
        // of collision (stop right at the collision).
        private static void resolveAabbCollision(Entity entity, Entity collideAgainst, double elapsedSeconds)
        {
            var movementComponent = entity.GetIfHas<FourWayMovementComponent>();
            (var oldIntendedX, var oldIntendedY) = (movementComponent.IntendedMoveDeltaX, movementComponent.IntendedMoveDeltaY);

            var entityCollision = entity.GetIfHas<CollisionComponent>();
            var collideAgainstComponent = collideAgainst.GetIfHas<CollisionComponent>();

            if (isAabbCollision(entity.X + movementComponent.IntendedMoveDeltaX, entity.Y + movementComponent.IntendedMoveDeltaY, entityCollision.Width, entityCollision.Height,
                collideAgainst.X, collideAgainst.Y, collideAgainstComponent.Width, collideAgainstComponent.Height))
            {
                // Another entity occupies that space. Use separating axis theorem (SAT)
                // to see how much we can move, and then move accordingly, resolving at whichever
                // axis collides first by time (not whichever one is the smallest diff).
                (float xDistance, float yDistance) = CalculateAabbDistanceTo(entity, collideAgainst);
                float xVelocity = (float)(movementComponent.IntendedMoveDeltaX / elapsedSeconds);
                float yVelocity = (float)(movementComponent.IntendedMoveDeltaY / elapsedSeconds);
                float xAxisTimeToCollide = xVelocity != 0 ? Math.Abs(xDistance / xVelocity) : 0;
                float yAxisTimeToCollide = yVelocity != 0 ? Math.Abs(yDistance / yVelocity) : 0;

                float shortestTime = 0;

                if (xVelocity != 0 && yVelocity == 0)
                {
                    // Colliison on X-axis only
                    shortestTime = xAxisTimeToCollide;
                    movementComponent.IntendedMoveDeltaX = shortestTime * xVelocity;                    
                }
                else if (xVelocity == 0 && yVelocity != 0)
                {
                    // Collision on Y-axis only
                    shortestTime = yAxisTimeToCollide;
                    movementComponent.IntendedMoveDeltaY = shortestTime * yVelocity;
                }
                else
                {
                    // Collision on X and Y axis (eg. slide up against a wall)
                    shortestTime = Math.Min(Math.Abs(xAxisTimeToCollide), Math.Abs(yAxisTimeToCollide));
                    movementComponent.IntendedMoveDeltaX = shortestTime * xVelocity;
                    movementComponent.IntendedMoveDeltaY = shortestTime * yVelocity;

                    if (movementComponent.SlideOnCollide)
                    {
                        // Setting oldIntendedX/oldIntendedY might put us directly inside another solid thing.
                        // No worries, we resolve collisions twice, so the second iteration will catch it.

                        // Resolved collision on the X-axis first
                        if (shortestTime == xAxisTimeToCollide)
                        {
                            // Slide vertically
                            movementComponent.IntendedMoveDeltaX = 0;
                            movementComponent.IntendedMoveDeltaY = oldIntendedY;
                        }
                        // Resolved collision on the Y-axis first
                        if (shortestTime == yAxisTimeToCollide)
                        {
                            // Slide horizontally
                            movementComponent.IntendedMoveDeltaX = oldIntendedX;
                            movementComponent.IntendedMoveDeltaY = 0;
                        }
                    }
                }        
            }
        }

        // Assuming we have two AABBs, what's the actual distance between them?
        // eg. if `e1` is on the left of `e2`, we want `dx` to be `e2.left - e1.right`.
        private static (float, float) CalculateAabbDistanceTo(Entity e1, Entity e2)
        {
            var movingCollision = e1.GetIfHas<CollisionComponent>();
            var targetCollision = e2.GetIfHas<CollisionComponent>();

            float dx = 0;
            float dy = 0;

            if (e1.X < e2.X)
            {
                dx = e2.X - (e1.X + movingCollision.Width);
            }
            else if (e1.X > e2.X)
            {
                dx = e1.X - (e2.X + targetCollision.Width);
            }
            
            if (e1.Y < e2.Y)
            {
                dy = e2.Y - (e1.Y + movingCollision.Height);
            }
            else if (e1.Y > e2.Y)
            {
                dy = e1.Y - (e2.Y + targetCollision.Height);
            }
            
            return (dx, dy);
        }

        private static bool isAabbCollision(float x1, float y1, int w1, int h1, float x2, float y2, int w2, int h2)
        {
            // Adapted from https://tutorialedge.net/gamedev/aabb-collision-detection-tutorial/
            return x1 < x2 + w2 &&
                x1 + w1 > x2 &&
                y1 < y2 + h2 &&
                y1 + h1 > y2;
        }
    }
}