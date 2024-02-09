//
// This file is part of the Frenetic Game Engine, created by Frenetic LLC.
// This code is Copyright (C) Frenetic LLC under the terms of a strict license.
// See README.md or LICENSE.txt in the FreneticGameEngine source root for the contents of the license.
// If neither of these are available, assume that neither you nor anyone other than the copyright holder
// hold any right or permission to use this software until such time as the official license is identified.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FGEGraphics.ClientSystem;
using FGEGraphics.GraphicsHelpers;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace FGEGraphics.UISystem;

/// <summary>Represents an entire screen with any kind of graphics.</summary>
public class UIScreen : UIElement
{
    /// <summary>The default priority of a UI Screen.</summary>
    public const double SCREEN_PRIORITY_DEFAULT = -10E10;

    /// <summary>
    /// A reference to the relevant client backing this screen.
    /// Get this using <see cref="Window"/>.
    /// </summary>
    private readonly GameClientWindow InternalClient;

    /// <summary>Gets the client game engine this screen is associated with.</summary>
    public override GameEngineBase Engine => InternalClient.CurrentEngine;

    /// <summary>Gets the client game window this screen is associated with.</summary>
    public override GameClientWindow Window => InternalClient;

    /// <summary>
    /// Whether to erase the screen at the beginning of each render call.
    /// <para>Generally only used if this UI is considered the dominant central point of a view.</para>
    /// </summary>
    protected bool ResetOnRender = false;

    /// <summary>Constructs a screen that covers the entire game window.</summary>
    /// <param name="view">The client UI View.</param>
    public UIScreen(ViewUI2D view) : this(view.Client, new UIPositionHelper(view))
    {
        Position.GetterWidth(() => Parent == null ? Engine.Window.ClientSize.X : Parent.Position.Width);
        Position.GetterHeight(() => Parent == null ? Engine.Window.ClientSize.Y : Parent.Position.Height);
        RenderPriority = SCREEN_PRIORITY_DEFAULT;
    }

    /// <summary>Constructs a screen that covers a specific portion of the game window.</summary>
    /// <param name="client">The client game window.</param>
    /// <param name="pos">The position of the element.</param>
    public UIScreen(GameClientWindow client, UIPositionHelper pos) : base(pos)
    {
        InternalClient = client;
        IsValid = true;
    }

    /// <summary>Performs a render on this element.</summary>
    /// <param name="view">The UI view.</param>
    /// <param name="delta">The time since the last render.</param>
    /// <param name="style">The current element style.</param>
    public override void Render(ViewUI2D view, double delta, UIElementStyle style)
    {
        if (ResetOnRender)
        {
            GL.ClearBuffer(ClearBuffer.Color, 0, new float[] { 0f, 0.5f, 0.5f, 1f });
            GL.ClearBuffer(ClearBuffer.Depth, 0, new float[] { 1f });
            GraphicsUtil.CheckError("RenderScreen - Reset");
        }
    }

    /// <summary>Preps the switch to this screen.</summary>
    public virtual void SwitchTo()
    {
    }

    /// <summary>Preps the switch from this screen.</summary>
    public virtual void SwitchFrom()
    {
    }
}
