using System.Drawing;

namespace Neko;

internal enum BehaviorKind
{
    FreeWander,
    WindowWalker,
}

internal interface IPetBehavior
{
    void Update(double deltaSeconds, Point cursorPos);
    PointF Position { get; }
    Size Size { get; }
    float Yaw { get; }
    PetState State { get; }
    bool SleepTwitch { get; }
}
