﻿using System;
using System.Collections.Generic;
using System.Linq;
using Puffin.Core.Ecs;
using Puffin.Core.Ecs.Systems;
using Puffin.Core.Events;
using Puffin.Core.IO;
using Puffin.Core.Tiles;
using Puffin.Core.Tweening;

namespace Puffin.Core
{
    /// <summary>
    /// A scene or screen in your game. This is where you add entities with functionality
    /// to implement your game's logic. Also contains event for mouse-click and key-press detection.
    /// </summary>
    public class Scene : IDisposable
    {
        /// <summary>
        /// The last recorded FPS (frames per second). This is the number of draw calls per second.
        /// This value updates approximately every second.
        /// </summary>
        public float Fps { get; private set; }

        /// <summary>
        /// The background colour of this scene in the format 0xRRGGBB; it is drawn before all other entities.
        /// </summary>
        public int BackgroundColour = 0x000000; // black

        /// <summary>
        /// The background image to render. Ignores camera, zoom, etc.
        /// </summary>
        public string Background { 
            get { return this._background; }
            set { 
                this._background = value;
                this.EventBus.Broadcast(EventBusSignal.BackgroundSet, _background);
            }
        }

        /// <summary>
        /// A scene-wide mouse-click handler that fires whever a mouse click event triggers (even if entities handle it).
        /// </summary>
        public Action<ClickType> OnMouseClick;

        /// <summary>
        /// A scene-wide on-action-key-pressed handler that fires whenever a key that maps to an action is just pressed.
        /// </summary>
        public Action<Enum> OnActionPressed;

        /// <summary>
        /// A scene-wide on-action-key-released handler that fires whenever a key that maps to an action is just released.
        /// </summary>
        public Action<Enum> OnActionReleased;

        internal EventBus EventBus = new EventBus();
        internal TweenManager TweenManager = new TweenManager();
        
        // Drawn in the order added. Internal because needed for collision resolution.
        internal List<TileMap> TileMaps = new List<TileMap>();
        internal bool CalledReady = false;
        internal Scene SubScene; // the one and only sub-scene we can show
        
        /// <summary>
        /// If this scene is a sub-scene, ParentScene is the owning/parent scene. Useful if nesting sub-scenes,
        /// or dynamically changing the sub-scene to a different sub-scene.
        /// </summary>
        internal protected Scene ParentScene; // the parent scene, if we're a sub-scene

        // Break update calls that have long elapsed times into chunks of this many milliseconds.
        private readonly float MAX_UPDATE_INTERVAL_SECONDS = 0.150f;

        private IMouseProvider mouseProvider;
        private IKeyboardProvider keyboardProvider;
        private ISystem[] systems = new ISystem[0];
        private DrawingSystem drawingSystem;
        private readonly List<Entity> entities = new List<Entity>();

        // A date and a number of draw calls to calculate FPS
        private DateTime lastFpsUpdate = DateTime.Now;
        private int drawsSinceLastFpsCount = 0;
        private string _background;

        /// <summary>
        /// The current mouse coordinates, from the perspective of the current camera (if one exists).
        /// eg. if your game is zoomed in 3x, (150, 51) in screen space returns (50, 17).
        /// This is useful to get things in "game space" irrespective of camera zoom.
        /// </summary>
        public Tuple<int, int> MouseCoordinates { get { return this.mouseProvider.MouseCoordinates; }}

        /// <summary>
        /// The current mouse coordinates in screen space, irrespective of the current camera (if one exists).
        /// This is useful for manual checks for UI entities.
        /// </summary>
        public Tuple<int, int> UiMouseCoordinates { get { return this.mouseProvider.UiMouseCoordinates; }}

        /// <summary>
        /// Creates a new, empty Scene instance.
        /// </summary>
        public Scene()
        {            
        }

        /// <summary>
        /// Adds an entity to the current scene so that it starts functioning (based on its components).
        /// </summary>
        public void Add(Entity entity)
        {
            this.entities.Add(entity);
            entity.Scene = this;
            
            // if initialized, notify systems
            if (this.systems.Length > 0)
            {
                foreach (var system in this.systems)
                {
                    system.OnAddEntity(entity);
                }
            }

            if (this.CalledReady)
            {
                entity.OnReadyAction?.Invoke();
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
            tileMap.Scene = this;
            this.TileMaps.Add(tileMap);
            this.drawingSystem?.OnAddTileMap(tileMap);
        }

        /// <summary>
        /// Removes a tilemap from the scene; it will no longer be drawn.
        /// </summary>
        public void Remove(TileMap tileMap)
        {
            this.TileMaps.Remove(tileMap);
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
        public virtual void Update(float elapsedSeconds)
        {
            this.keyboardProvider.Update();
            // Mouse provider is updated through mouse system, so we can coordinate and
            // allow it to invoke general callbacks if an event was unhandled.
            this.TweenManager.Update(elapsedSeconds);
        }

        /// <summary>
        /// A method that's called when the game is ready to run and content can be loaded.
        /// This includes being able to change label font sizes.
        /// </summary>
        public virtual void Ready()
        {
            foreach (var entity in this.entities)
            {
                entity.OnReadyAction?.Invoke();
            }
            this.CalledReady = true;
        }

        /// <summary>
        /// Tweens an entity from a start position to an end position over a period of time; optionally, invokes a callback when the tween completes.
        /// Note that this immediately moves the entity to the specified start position.
        /// </summary>
        public void TweenPosition(Entity entity, Tuple<float, float> startPosition, Tuple<float, float> endPosition, float durationSeconds, Action onTweenComplete = null)
        {
            this.TweenManager.Tween(entity, durationSeconds, startPosition, endPosition, 1, 1, onTweenComplete);
        }

        /// <summary>
        /// Tween the alpha of an entity over a period of time. Note that this immediately sets the alpha to <c>startAlpha</c>.
        /// </summary>
        public void TweenAlpha(Entity entity, float startAlpha, float endAlpha, float durationSeconds, Action onTweenComplete = null)
        {
            var position = new Tuple<float, float>(entity.X, entity.Y);
            this.TweenManager.Tween(entity, durationSeconds, position, position, startAlpha, endAlpha, onTweenComplete);
        }

        /// <summary>
        /// Sets and displays a sub-scene. This scene is rendered above the current scene.
        /// The current scene stops processing input and updates.
        /// </summary>
        public void ShowSubScene(Scene subScene)
        {
            subScene.ParentScene = this;

            this.HideSubScene();
            this.SubScene = subScene;
            this.EventBus.Broadcast(EventBusSignal.SubSceneShown, subScene);
            
            if (!subScene.CalledReady)
            {
                subScene.Ready();
            }
        }

        /// <summary>
        /// Unsets/removes the current sub-scene. The current scene resumes receiving input/updates.
        /// </summary>
        public virtual void HideSubScene()
        {
            if (this.SubScene != null)
            {
                this.EventBus.Broadcast(EventBusSignal.SubSceneHidden, this.SubScene);
                this.SubScene.Deinitialize();
                this.SubScene = null;

                // Fix a bug where the background disappears when you hide a subscene; reload the (disposed) instance
                if (!string.IsNullOrWhiteSpace(this.Background))
                {
                    this.EventBus.Broadcast(EventBusSignal.BackgroundSet, this.Background);
                }
            }

            // Click-hold / keypress on the parent scene. If that triggers something on the child scene, NO, don't trigger.
            this.keyboardProvider.Reset();
        }
        
        /// <summary>
        /// Disposes the scene and the event bus (so entities can be garbage-collected).
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            this.EventBus?.Dispose();
        }

        virtual public void OnDispose()
        {
            
        }

        internal void FlushToScreen()
        {
            this.drawingSystem.FlushToScreen();
        }

        // "Macro" method, deliver updates in chunks of <= 150ms (MAX_UPDATE_INTERVAL_MILLISECONDS).
        // This gives our games more stability, especially with physics, or collision
        // detection with fast speeds and/or large velocities and/or intervals.
        internal void OnUpdate(TimeSpan elapsed)
        {
            var secondsLeft = (float)elapsed.TotalSeconds;
            while (secondsLeft > 0)
            {
                if (secondsLeft >= MAX_UPDATE_INTERVAL_SECONDS)
                {
                    this.OnUpdate(MAX_UPDATE_INTERVAL_SECONDS);
                    this.Update(MAX_UPDATE_INTERVAL_SECONDS);
                    secondsLeft -= MAX_UPDATE_INTERVAL_SECONDS;
                }
                else
                {
                    this.OnUpdate(secondsLeft);
                    this.Update(secondsLeft);
                    secondsLeft = 0;
                }
            }

            var timeDiff = (DateTime.Now - lastFpsUpdate).TotalSeconds;
            if (timeDiff >= 1)
            {
                this.Fps = (float)(drawsSinceLastFpsCount / timeDiff);
                this.drawsSinceLastFpsCount = 0;
                this.lastFpsUpdate = DateTime.Now;
                //Console.WriteLine($"{Fps} fps");
            }
        }

        internal void OnDraw(TimeSpan elapsed, bool clearDisplay)
        {
            if (clearDisplay)
            {
                // Actual draw, not a subscene draw
                drawsSinceLastFpsCount++;
            }
            
            this.drawingSystem.OnDraw(elapsed, this.BackgroundColour, this.Background, clearDisplay);
        }

        // Separate from the constructor and internal because only we call it; subclasses of Scene don't need to know about this.
        /// Sets up all the systems and reports to them all the entities/tilemaps/etc.
        internal void Initialize(ISystem[] systems, IMouseProvider mouseProvider, IKeyboardProvider keyboardProvider)
        {
            this.drawingSystem = systems.Single(s => s is DrawingSystem) as DrawingSystem;
            this.systems = systems;
            
            this.mouseProvider = mouseProvider;
            this.keyboardProvider = keyboardProvider;

            this.EventBus.Subscribe(EventBusSignal.MouseClicked, (o) => this.OnMouseClick?.Invoke((ClickType)o));
            this.EventBus.Subscribe(EventBusSignal.ActionPressed, (o) => this.OnActionPressed?.Invoke(o as Enum));
            this.EventBus.Subscribe(EventBusSignal.ActionReleased, (o) => this.OnActionReleased?.Invoke(o as Enum));

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

            if (!this.CalledReady)
            {
                this.Ready();
            }
        }

        private void Deinitialize()
        {
            // Called by subscenes when they die
            foreach (var entity in this.entities)
            {
                foreach (var system in this.systems)
                {
                    system.OnRemoveEntity(entity);
                }
            }

            // Initialize tilemaps' sprites
            foreach (var tileMap in this.TileMaps)
            {
                this.drawingSystem.OnRemoveTileMap(tileMap);
            }

            this.systems = new ISystem[0];
            this.entities.Clear();
            this.TileMaps.Clear();
            this.EventBus.Dispose();
        }

        // "Micro" method, called with chunks of time <= MAX_UPDATE_INTERVAL_MILLISECONDS
        private void OnUpdate(float elapsedSeconds)
        {
            foreach (var system in this.systems)
            {
                system.OnUpdate(TimeSpan.FromSeconds(elapsedSeconds));
            }

            foreach (var entity in this.entities)
            {
                foreach (var action in entity.OnUpdateActions)
                {
                    action.Invoke(elapsedSeconds);
                }
            }

            this.Update(elapsedSeconds);
        }
    }
}
