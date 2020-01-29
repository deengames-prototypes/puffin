﻿using System;
using System.Collections.Generic;
using System.Linq;
using Puffin.Core.Ecs;
using Puffin.Core.Ecs.Systems;
using Puffin.Core.IO;
using Puffin.Core.Tiles;

namespace Puffin.Core
{
    /// <summary>
    /// A scene or screen in your game. This is where you add entities with functionality
    /// to implement your game's logic.
    /// </summary>
    public class Scene : IDisposable
    {
        internal static Scene LatestInstance { get; private set; }

        /// <summary>
        /// The last recorded FPS (frames per second). This is the number of draw calls per second.
        /// This value updates approximately every second.
        /// </summary>
        public float Fps { get; private set; }

        /// <summary>
        /// The background colour of this scene in the format 0xRRGGBB; it is drawn before all other entities.
        /// </summary>
        public int BackgroundColour = 0x000000; // black

        // Used for a scene-only mouse click event
        internal Action OnMouseClick;

        // Drawn in the order added. Internal because needed for collision resolution.
        internal List<TileMap> TileMaps = new List<TileMap>();

        // Break update calls that have long elapsed times into chunks of this many milliseconds.
        private readonly int MAX_UPDATE_INERVAL_MILLISECONDS = 150;

        private IMouseProvider mouseProvider;
        private IKeyboardProvider keyboardProvider;
        private ISystem[] systems = new ISystem[0];
        private DrawingSystem drawingSystem;
        private List<Entity> entities = new List<Entity>();

        // A date and a number of draw calls to calculate FPS
        private DateTime lastFpsUpdate = DateTime.Now;
        private int drawsSinceLastFpsCount = 0;

        /// <summary>
        /// The current mouse coordinates.
        /// </summary>
        public Tuple<int, int> MouseCoordinates { get { return this.mouseProvider.MouseCoordinates; }}

        /// <summary>
        /// Creates a new, empty Scene instance.
        /// </summary>
        public Scene()
        {
            Scene.LatestInstance = this;
            EventBus.LatestInstance.Subscribe(EventBusSignal.MouseClicked, onMouseClick);
        }

        /// <summary>
        /// Adds an entity to the current scene so that it starts functioning (based on its components).
        /// </summary>
        public void Add(Entity entity)
        {
            this.entities.Add(entity);
            
            // if initialized, notify systems
            if (this.systems.Length > 0)
            {
                foreach (var system in this.systems)
                {
                    system.OnAddEntity(entity);
                }
            }
        }

        /// <summary>
        /// Remove an entity from the scene; it will no longer be rendered, updated, etc.
        /// </summary>
        public void Remove(Entity entity)
        {
            this.entities.Remove(entity);
            foreach (var system in this.systems)
            {
                system.OnRemoveEntity(entity);
            }
        }

        /// <summary>
        /// Adds a tilemap to the scene; it will be drawn. Any tiles with no value set, won't be drawn.
        /// Note that tilemaps are drawn in the order added.
        /// </summary>
        public void Add(TileMap tileMap)
        {
            this.TileMaps.Add(tileMap);
            if (this.drawingSystem != null)
            {
                this.drawingSystem.OnAddTileMap(tileMap);
            }
        }

        /// <summary>
        /// Removes a tilemap from the scene; it will no longer be drawn.
        /// </summary>
        public void Remove(TileMap tileMap)
        {
            this.drawingSystem.OnRemoveTileMap(tileMap);
        }

        /// <summary>
        /// Return true if any of a specific action's keys are pressed.
        /// </summary>        
        public bool IsActionDown(Enum action)
        {
            return this.keyboardProvider.IsActionDown(action);
        }

        /// <summary>
        /// A method that's called every time Update is called by the game engine.
        /// Override it to do things "every frame."
        /// </summary>
        public virtual void Update(int elapsedMilliseconds)
        {
            
        }
        
        /// <summary>
        /// Disposes the scene and the event bus (so entities can be garbage-collected).
        /// </summary>
        public void Dispose()
        {
            if (EventBus.LatestInstance != null)
            {
                EventBus.LatestInstance.Dispose();
            }

            // Reset EventBus.LatestIntance
            new EventBus();
        }

        // "Macro" method, deliver updates in chunks of <= 150ms (MAX_UPDATE_INTERVAL_MILLISECONDS).
        // This gives our games more stability, especially with physics, or collision
        // detection with fast speeds and/or large velocities and/or intervals.
        internal void OnUpdate(TimeSpan elapsed)
        {
            
            var secondsLeft = elapsed.TotalMilliseconds;
            while (secondsLeft > 0)
            {
                if (secondsLeft >= MAX_UPDATE_INERVAL_MILLISECONDS)
                {
                    this.OnUpdate(MAX_UPDATE_INERVAL_MILLISECONDS);
                    this.Update(MAX_UPDATE_INERVAL_MILLISECONDS);
                    secondsLeft -= MAX_UPDATE_INERVAL_MILLISECONDS;
                }
                else
                {
                    this.OnUpdate((int)secondsLeft);
                    this.Update((int)secondsLeft);
                    secondsLeft = 0;
                }
            }

            var timeDiff = (DateTime.Now - lastFpsUpdate).TotalSeconds;
            if (timeDiff >= 1)
            {
                this.Fps = (float)(drawsSinceLastFpsCount / timeDiff);
                this.drawsSinceLastFpsCount = 0;
                this.lastFpsUpdate = DateTime.Now;
                Console.WriteLine($"{Fps} fps");
            }
        }

        internal void OnDraw(TimeSpan elapsed)
        {
            drawsSinceLastFpsCount++;
            this.drawingSystem.OnDraw(elapsed, this.BackgroundColour);
        }

        // Separate from the constructor and internal because only we call it; subclasses of Scene don't need to know about this.
        /// Sets up all the systems and reports to them all the entities/tilemaps/etc.
        internal void Initialize(ISystem[] systems, IMouseProvider mouseProvider, IKeyboardProvider keyboardProvider)
        {
            this.drawingSystem = systems.Single(s => s is DrawingSystem) as DrawingSystem;
            this.systems = systems;
            
            this.mouseProvider = mouseProvider;
            this.keyboardProvider = keyboardProvider;

            // If called after AddEntity, add entities we know about
            foreach (var entity in this.entities)
            {
                foreach (var system in this.systems)
                {
                    system.OnAddEntity(entity);
                }
            }

            // Initialize tilemaps' sprites
            foreach (var tileMap in this.TileMaps)
            {
                this.drawingSystem.OnAddTileMap(tileMap);
            }
        }

        // "Micro" method, called with chunks of time <= MAX_UPDATE_INTERVAL_MILLISECONDS
        private void OnUpdate(int elapsedMilliseconds)
        {
            foreach (var system in this.systems)
            {
                system.OnUpdate(TimeSpan.FromMilliseconds(elapsedMilliseconds));
            }

            this.Update(elapsedMilliseconds);
        }

        private void onMouseClick(object data)
        {
            if (this.OnMouseClick != null)
            {
                this.OnMouseClick.Invoke();
            }
        }
    }
}
