using System;

namespace Threads.UiFrames
{
    /// <summary>
    /// Names a command. The built-in ids are static fields; any package can define its own
    /// (<c>new FrameCommandId("mypkg.spin")</c>) and its frame kinds handle them. Ids compare by name.
    /// </summary>
    public readonly struct FrameCommandId : IEquatable<FrameCommandId>
    {
        public readonly string Name;

        public FrameCommandId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A command id needs a name.", nameof(name));
            Name = name;
        }

        /// <summary>Open a drawer.</summary>
        public static readonly FrameCommandId Open = new FrameCommandId("open");
        /// <summary>Close a drawer.</summary>
        public static readonly FrameCommandId Close = new FrameCommandId("close");
        /// <summary>Open a closed drawer, close an open one.</summary>
        public static readonly FrameCommandId Toggle = new FrameCommandId("toggle");
        /// <summary>Step forward by one item (a card), or by a page when there are no items.</summary>
        public static readonly FrameCommandId Next = new FrameCommandId("next");
        /// <summary>Step back by one item (a card), or by a page when there are no items.</summary>
        public static readonly FrameCommandId Previous = new FrameCommandId("previous");
        /// <summary>Step forward by a page (the visible cards).</summary>
        public static readonly FrameCommandId PageNext = new FrameCommandId("page-next");
        /// <summary>Step back by a page (the visible cards).</summary>
        public static readonly FrameCommandId PagePrevious = new FrameCommandId("page-previous");
        /// <summary>Go to the start.</summary>
        public static readonly FrameCommandId Home = new FrameCommandId("home");
        /// <summary>Go to the end.</summary>
        public static readonly FrameCommandId End = new FrameCommandId("end");
        /// <summary>Go to <see cref="FrameCommand.Index"/>.</summary>
        public static readonly FrameCommandId GoTo = new FrameCommandId("go-to");
        /// <summary>Animated scroll by <see cref="FrameCommand.DeltaDp"/> (mouse wheel, keys).</summary>
        public static readonly FrameCommandId ScrollBy = new FrameCommandId("scroll-by");
        /// <summary>A direct manipulation starts (finger or mouse button down and moved).</summary>
        public static readonly FrameCommandId DragBegin = new FrameCommandId("drag-begin");
        /// <summary>The direct manipulation moved by <see cref="FrameCommand.DeltaDp"/>.</summary>
        public static readonly FrameCommandId DragMove = new FrameCommandId("drag-move");
        /// <summary>The direct manipulation ended with <see cref="FrameCommand.VelocityDps"/>.</summary>
        public static readonly FrameCommandId DragEnd = new FrameCommandId("drag-end");
        /// <summary>The direct manipulation was cancelled; the frame settles where it was.</summary>
        public static readonly FrameCommandId DragCancel = new FrameCommandId("drag-cancel");
        /// <summary>Bring <see cref="FrameCommand.Payload"/> (an <see cref="IFrameElement"/> inside the
        /// frame, usually the element that just got UI focus) into view: a strip scrolls the least it
        /// can, a deck steps to its card, a closed drawer opens.</summary>
        public static readonly FrameCommandId Reveal = new FrameCommandId("reveal");
        /// <summary>Keyboard/gamepad arrow across the frame's axis while one of its cards is selected:
        /// <see cref="FrameCommand.Index"/> is +1 (right or down) or −1 (left or up) and
        /// <see cref="FrameCommand.Payload"/> is the card. Built-in frames ignore it; a menu page uses
        /// it to change the selected row's value (Left/Right on a slider or cycler).</summary>
        public static readonly FrameCommandId Adjust = new FrameCommandId("adjust");
        /// <summary>Keyboard/gamepad Enter on a card with nothing inside to go to:
        /// <see cref="FrameCommand.Payload"/> is the card. Sent to the card's frame (before the
        /// navigator's <c>Activated</c> event); a menu page toggles, presses or opens the row.</summary>
        public static readonly FrameCommandId Activate = new FrameCommandId("activate");

        public bool Equals(FrameCommandId other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is FrameCommandId other && Equals(other);
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
        public static bool operator ==(FrameCommandId a, FrameCommandId b) => a.Equals(b);
        public static bool operator !=(FrameCommandId a, FrameCommandId b) => !a.Equals(b);
        public override string ToString() => Name ?? "(none)";
    }

    /// <summary>
    /// One command for a frame. Commands are device-neutral: a swipe, a mouse drag, an arrow key
    /// or a gamepad button all end up as one of these. Distances are in dp along the frame's axis,
    /// in the frame's positive direction: for a drawer, towards open; for strips and decks,
    /// towards later content. Controllers convert screen motion with <see cref="FrameAxisMath"/>.
    /// </summary>
    public readonly struct FrameCommand
    {
        public readonly FrameCommandId Id;
        public readonly int Index;
        public readonly float DeltaDp;
        public readonly float VelocityDps;
        /// <summary>Anything else a package-defined command needs.</summary>
        public readonly object Payload;

        public FrameCommand(FrameCommandId id, int index = 0, float deltaDp = 0f, float velocityDps = 0f, object payload = null)
        {
            Id = id;
            Index = index;
            DeltaDp = deltaDp;
            VelocityDps = velocityDps;
            Payload = payload;
        }

        public static FrameCommand Open => new FrameCommand(FrameCommandId.Open);
        public static FrameCommand Close => new FrameCommand(FrameCommandId.Close);
        public static FrameCommand Toggle => new FrameCommand(FrameCommandId.Toggle);
        public static FrameCommand Next => new FrameCommand(FrameCommandId.Next);
        public static FrameCommand Previous => new FrameCommand(FrameCommandId.Previous);
        public static FrameCommand PageNext => new FrameCommand(FrameCommandId.PageNext);
        public static FrameCommand PagePrevious => new FrameCommand(FrameCommandId.PagePrevious);
        public static FrameCommand Home => new FrameCommand(FrameCommandId.Home);
        public static FrameCommand End => new FrameCommand(FrameCommandId.End);
        public static FrameCommand DragBegin => new FrameCommand(FrameCommandId.DragBegin);
        public static FrameCommand DragCancel => new FrameCommand(FrameCommandId.DragCancel);
        public static FrameCommand GoTo(int index) => new FrameCommand(FrameCommandId.GoTo, index: index);
        public static FrameCommand ScrollBy(float deltaDp) => new FrameCommand(FrameCommandId.ScrollBy, deltaDp: deltaDp);
        public static FrameCommand DragMove(float deltaDp) => new FrameCommand(FrameCommandId.DragMove, deltaDp: deltaDp);
        public static FrameCommand DragEnd(float velocityDps) => new FrameCommand(FrameCommandId.DragEnd, velocityDps: velocityDps);
        public static FrameCommand Reveal(IFrameElement element) => new FrameCommand(FrameCommandId.Reveal, payload: element);
        public static FrameCommand Adjust(int direction, IFrameElement card) => new FrameCommand(FrameCommandId.Adjust, index: direction, payload: card);
        public static FrameCommand Activate(IFrameElement card) => new FrameCommand(FrameCommandId.Activate, payload: card);

        public override string ToString()
        {
            if (Id == FrameCommandId.GoTo) return $"go-to({Index})";
            if (Id == FrameCommandId.ScrollBy || Id == FrameCommandId.DragMove) return $"{Id}({DeltaDp})";
            if (Id == FrameCommandId.DragEnd) return $"drag-end({VelocityDps})";
            return Id.ToString();
        }
    }

    /// <summary>Anything a controller can send commands to.</summary>
    public interface IFrameCommandTarget
    {
        /// <summary>Runs the command. Returns false if this frame does not handle that id, so a
        /// controller can pass it on (e.g. to an outer frame).</summary>
        bool Execute(in FrameCommand command);
    }
}
