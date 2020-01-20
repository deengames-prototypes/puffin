using Puffin.Core;
using Puffin.Core.Ecs.Systems;
using Puffin.Infrastructure.MonoGame.Drawing;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Puffin.Infrastructure.MonoGame.IO;
using Puffin.Core.IO;
using Ninject;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using System;

namespace Puffin.Infrastructure.MonoGame
{
    public class PuffinGame : Game
    {
        /// <summary>
        /// A mapping of in-game actions to the (MonoGame) keyboard keys that map to them.
        /// PuffinGame ships with default mappings for all actions; you can override these
        /// to change keyboard bindings, or expose them in a UI and allow users to arbitrarily
        /// override keyboard mappings (for accessibility).
        /// <summary>
        public Dictionary<Enum, List<Keys>> actionToKeys = new Dictionary<Enum, List<Keys>>() {
            { PuffinAction.Up, new List<Keys>() { Keys.W, Keys.Up } },
            { PuffinAction.Down, new List<Keys>() { Keys.S, Keys.Down } },
            { PuffinAction.Left, new List<Keys>() { Keys.A, Keys.Left } },
            { PuffinAction.Right, new List<Keys>() { Keys.D, Keys.Right } },
        };

        public static PuffinGame LatestInstance;

        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private SpriteFont defaultFont;
        private Scene currentScene;
        private IMouseProvider mouseProvider;
        private IKeyboardProvider keyboardProvider;


        public PuffinGame()
        {
            PuffinGame.LatestInstance = this;
            this.graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            DependencyInjection.Kernel.Bind<IMouseProvider>().To<MonoGameMouseProvider>().InSingletonScope();
            this.mouseProvider = DependencyInjection.Kernel.Get<IMouseProvider>();

            DependencyInjection.Kernel.Bind<IKeyboardProvider>().To<MonoGameKeyboardProvider>().InSingletonScope();
            this.keyboardProvider = DependencyInjection.Kernel.Get<IKeyboardProvider>();
        }

        public void ShowScene(Scene s)
        {
            if (this.currentScene != null)
            {
                this.currentScene.Dispose();
            }
            
            var drawingSurface = new MonoGameDrawingSurface(this.GraphicsDevice, spriteBatch, this.defaultFont);

            var systems = new ISystem[]
            {
                new DrawingSystem(drawingSurface),
            };

            s.Initialize(systems, this.mouseProvider);

            this.currentScene = s;
        }

        virtual protected void Ready()
        {

        }

        protected override void Initialize()
        {
            // TODO: Add your initialization logic here
            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            // Not used since we currently load sprites outside the pipeline
            this.defaultFont = Content.Load<SpriteFont>("OpenSans");

            this.Ready();
        }

        protected override void Update(GameTime gameTime)
        {
            //if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            //   Exit();

            // TODO: Add your update logic here
            this.mouseProvider.Update();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // TODO: Add your drawing code here
            if (this.currentScene != null)
            {
                this.currentScene.OnUpdate();
            }

            base.Draw(gameTime);
        }
    }
}