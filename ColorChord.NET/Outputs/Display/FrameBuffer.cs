using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Outputs.Display
{
    public class FrameBuffer // TODO: Cleanup
    {
        private readonly int Handle;
        private int TextureHandle;
        private int DepthHandle;
        private readonly bool IsDepthTexture;

        private int ColorWidth, ColorHeight;

        public Color4 ClearColour = new Color4(1F, 0F, 1F, 0F); // Transparent?

        /// <summary> Use this ctor if you don't intend to use the depth information as a texture. </summary>
        public FrameBuffer(int width, int height)
        {
            this.IsDepthTexture = false;
            this.ColorWidth = width;
            this.ColorHeight = height;
            this.Handle = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.Handle);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            this.TextureHandle = CreateTexture(width, height);
            this.DepthHandle = CreateDepthBuffer(width, height);
            Unbind();
        }

        /// <summary> Use this ctor if you do intend to use the depth information as a texture. </summary>
        public FrameBuffer(int colorWidth, int colorHeight, int depthWidth, int depthHeight)
        {
            this.IsDepthTexture = true;
            this.ColorWidth = colorWidth;
            this.ColorHeight = colorHeight;
            this.Handle = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.Handle);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            this.TextureHandle = CreateTexture(colorWidth, colorHeight);
            this.DepthHandle = CreateDepthTexture(depthWidth, depthHeight);
            Unbind();
        }

        /// <summary> Clears the FBO's colour data with the colour specified by <see cref="ClearColour"/>, and clears the depth data. </summary>
        public void Clear()
        {
            GL.ClearColor(this.ClearColour.R, this.ClearColour.G, this.ClearColour.B, this.ClearColour.A); // For some reason this doesn't work when I pass it in directly?
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.ClearColor(1F, 0.5F, 0F, 1F);
        }

        /// <summary> Sets the framebuffer binding to this one, with this FBO's dimensions. </summary>
        public void Bind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.Handle);
            GL.Viewport(0, 0, this.ColorWidth, this.ColorHeight);
        }

        /// <summary> Resets the current binding to the default framebuffer. </summary>
        /// <remarks> Make sure to set the canvas size back to the window size! </remarks>
        public void Unbind() => GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        /// <summary> Activates the FBO's colour buffer as the currently active texture. </summary>
        /// <param name="unit"> The texture unit to bind to. </param>
        public void UseTextureColor(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);
        }

        /// <summary> Activates the FBO's depth buffer as the currently active texture. </summary>
        /// <remarks> Only available when this FBO was initialized with depth buffer texture capability (4-arg ctor). </remarks>
        /// <param name="unit"> The texture unit to bind to. </param>
        public void UseTextureDepth(TextureUnit unit = TextureUnit.Texture0)
        {
            if (!this.IsDepthTexture) { throw new InvalidOperationException("Cannot use non-texture depth buffer as texture."); }
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, this.DepthHandle);
        }

        /// <summary> Saves the data contained in the colour buffer to a given streamas a PNG image. </summary>
        /// <param name="outputStream"> The stream to write the image data to. </param>
        [Obsolete("Not implemented here to prevent extra dependencies.")]
        public void SaveColorData(Stream outputStream) { }
        /*{
            Bind();
            byte[] Data = new byte[this.ColorWidth * this.ColorHeight * 4];
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.ReadPixels(0, 0, this.ColorWidth, this.ColorHeight, PixelFormat.Rgba, PixelType.Byte, Data);
            Image<Rgba32> OutputImg = new Image<Rgba32>(this.ColorWidth, this.ColorHeight);
            int Counter = 0;
            for (int x = 0; x < this.ColorWidth; x++)
            {
                for (int y = 0; y < this.ColorHeight; y++)
                {
                    Rgba32 Pixel = OutputImg[x, y];
                    Pixel.R = Data[Counter++];
                    Pixel.G = Data[Counter++];
                    Pixel.B = Data[Counter++];
                    Pixel.A = Data[Counter++];
                    OutputImg[x, y] = Pixel;
                }
            }
            OutputImg.SaveAsPng(outputStream);
            Unbind();
        }*/

        /// <summary> Resizes the framebuffer to a new size, affecting the colour buffer, texture, and depth buffer. If depth texture is enabled, its size is set as well. </summary>
        /// <param name="newWidth"> The new width to use, in pixels. </param>
        /// <param name="newHeight"> The new height to use, in pixels. </param>
        public void Resize(int newWidth, int newHeight)
        {
            this.ColorWidth = newWidth;
            this.ColorHeight = newHeight;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.Handle);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            GL.DeleteTexture(this.TextureHandle);
            this.TextureHandle = CreateTexture(newWidth, newHeight);

            if (!this.IsDepthTexture)
            {
                GL.DeleteRenderbuffer(this.DepthHandle);
                this.DepthHandle = CreateDepthBuffer(newWidth, newHeight);
            }
            else
            {
                GL.DeleteTexture(this.DepthHandle);
                this.DepthHandle = CreateDepthTexture(newWidth, newHeight);
            }
            Unbind();
        }

        /// <summary> Resizes the framebuffer to a new size, affecting the colour buffer, texture, as well as the depth buffer and texture. Not vaild if depth texture is not enabled. </summary>
        /// <param name="newColourWidth"> The new width to use for the colour buffer and texture, in pixels. </param>
        /// <param name="newColourHeight"> The new height to use for the colour buffer and texture, in pixels. </param>
        /// <param name="newDepthWidth"> The new width to use for the depth buffer and texture, in pixels. </param>
        /// <param name="newDepthHeight"> The new height to use for the depth buffer and texture, in pixels. </param>
        public void Resize(int newColourWidth, int newColourHeight, int newDepthWidth, int newDepthHeight)
        {
            if (!this.IsDepthTexture) { throw new InvalidOperationException("Cannot resize texture for non-texture depth buffer."); }
            this.ColorWidth = newColourWidth;
            this.ColorHeight = newColourHeight;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.Handle);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            GL.DeleteTexture(this.TextureHandle);
            this.TextureHandle = CreateTexture(newColourWidth, newColourHeight);
            GL.DeleteTexture(this.DepthHandle);
            this.DepthHandle = CreateDepthTexture(newDepthWidth, newDepthHeight);
            Unbind();
        }

        /// <summary> Sets up a texture for use with the colour buffer. </summary>
        /// <returns> The texture handle. </returns>
        private int CreateTexture(int width, int height)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            int TexHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, TexHandle);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, TexHandle, 0);
            return TexHandle;
        }

        /// <summary> Sets up a texture for the depth buffer. </summary>
        /// <returns> The texture handle. </returns>
        private int CreateDepthTexture(int width, int height)
        {
            int DepthHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, DepthHandle);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, DepthHandle, 0);
            return DepthHandle;
        }

        /// <summary> Sets up a non-texture buffer for depth data. </summary>
        /// <returns> The render buffer handle. </returns>
        private int CreateDepthBuffer(int width, int height)
        {
            int DepthHandle = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthHandle);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, DepthHandle);
            return DepthHandle;
        }
    }
}
