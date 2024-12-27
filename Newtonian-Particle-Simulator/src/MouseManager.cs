using OpenTK.Input;
using OpenTK;

namespace Newtonian_Particle_Simulator
{
    static class MouseManager
    {
        private static MouseState lastMouseState;
        private static MouseState thisMouseState;

        public static int WindowPositionX => thisMouseState.X;
        public static int WindowPositionY => thisMouseState.Y;

        public static ButtonState LeftButton => thisMouseState.LeftButton;
        public static ButtonState RightButton => thisMouseState.RightButton;

        public static Vector2 DeltaPosition => new Vector2(thisMouseState.X - lastMouseState.X, thisMouseState.Y - lastMouseState.Y);

        public static void Update()
        {
            lastMouseState = thisMouseState;
            thisMouseState = Mouse.GetState();
        }

        /// <summary>
        /// Returns true only on the first frame the button is pressed
        /// </summary>
        public static bool IsButtonTouched(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:
                    return thisMouseState.LeftButton == ButtonState.Pressed && lastMouseState.LeftButton == ButtonState.Released;
                case MouseButton.Right:
                    return thisMouseState.RightButton == ButtonState.Pressed && lastMouseState.RightButton == ButtonState.Released;
                default:
                    return false;
            }
        }
    }
}
