using System;

namespace Production.Weapons
{
    /// <summary>A colour as four floats: red, green, blue and alpha, each from 0 to 1 by convention (nothing here clamps them: the catalogue says what they are).
    /// It is how the data of a weapon carries the colour of its tracer, its flash, its reticle and its grade. The package never draws: whoever does
    /// turns an <see cref="Rgba"/> into the colour type of its own engine.</summary>
    public readonly struct Rgba
    {
        public readonly float r, g, b, a;

        public Rgba(float r, float g, float b, float a = 1f)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }

        /// <summary>The components in the order red, green, blue, alpha.</summary>
        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: throw new IndexOutOfRangeException("Invalid Rgba index(" + index + ")!");
                }
            }
        }
    }
}
