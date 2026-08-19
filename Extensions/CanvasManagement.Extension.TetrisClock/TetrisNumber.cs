using System.Collections.ObjectModel;

namespace CanvasManagement.Extension.TetrisClock;

internal class TetrisNumber
{
    // *********************************************************************
    // Fall instructions for all numbers
    // *********************************************************************

    // *********************************************************************
    // Number 0
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number0 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(2, 5, 4, 16, 0),
        new AnimationFragment(4, 7, 2, 16, 1),
        new AnimationFragment(3, 4, 0, 16, 1),
        new AnimationFragment(6, 6, 1, 16, 1),
        new AnimationFragment(5, 1, 4, 14, 0),
        new AnimationFragment(6, 6, 0, 13, 3),
        new AnimationFragment(5, 1, 4, 12, 0),
        new AnimationFragment(5, 1, 0, 11, 0),
        new AnimationFragment(6, 6, 4, 10, 1),
        new AnimationFragment(6, 6, 0, 9, 1),
        new AnimationFragment(5, 1, 1, 8, 1),
        new AnimationFragment(2, 5, 3, 8, 3)
    });

    // *********************************************************************
    // Number 1
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number1 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(2, 5, 4, 16, 0),
        new AnimationFragment(3, 4, 4, 15, 1),
        new AnimationFragment(3, 4, 5, 13, 3),
        new AnimationFragment(2, 5, 4, 11, 2),
        new AnimationFragment(0, 0, 4, 8, 0)
    });

    // *********************************************************************
    // Number 2
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number2 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(0, 0, 4, 16, 0),
        new AnimationFragment(3, 4, 0, 16, 1),
        new AnimationFragment(1, 2, 1, 16, 3),
        new AnimationFragment(1, 2, 1, 15, 0),
        new AnimationFragment(3, 4, 1, 12, 2),
        new AnimationFragment(1, 2, 0, 12, 1),
        new AnimationFragment(2, 5, 3, 12, 3),
        new AnimationFragment(0, 0, 4, 10, 0),
        new AnimationFragment(3, 4, 1, 8, 0),
        new AnimationFragment(2, 5, 3, 8, 3),
        new AnimationFragment(1, 2, 0, 8, 1)
    });

    // *********************************************************************
    // Number 3
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number3 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(1, 2, 3, 16, 3),
        new AnimationFragment(2, 5, 0, 16, 1),
        new AnimationFragment(3, 4, 1, 15, 2),
        new AnimationFragment(0, 0, 4, 14, 0),
        new AnimationFragment(3, 4, 1, 12, 2),
        new AnimationFragment(1, 2, 0, 12, 1),
        new AnimationFragment(3, 4, 5, 12, 3),
        new AnimationFragment(2, 5, 3, 11, 0),
        new AnimationFragment(3, 4, 1, 8, 0),
        new AnimationFragment(1, 2, 0, 8, 1),
        new AnimationFragment(2, 5, 3, 8, 3)
    });

    // *********************************************************************
    // Number 4
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number4 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(0, 0, 4, 16, 0),
        new AnimationFragment(0, 0, 4, 14, 0),
        new AnimationFragment(3, 4, 1, 12, 0),
        new AnimationFragment(1, 2, 0, 12, 1),
        new AnimationFragment(2, 5, 0, 10, 0),
        new AnimationFragment(2, 5, 3, 12, 3),
        new AnimationFragment(3, 4, 4, 10, 3),
        new AnimationFragment(2, 5, 0, 9, 2),
        new AnimationFragment(3, 4, 5, 10, 1)
    });

    // *********************************************************************
    // Number 5
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number5 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(0, 0, 0, 16, 0),
        new AnimationFragment(2, 5, 2, 16, 1),
        new AnimationFragment(2, 5, 3, 15, 0),
        new AnimationFragment(3, 4, 5, 16, 1),
        new AnimationFragment(3, 4, 1, 12, 0),
        new AnimationFragment(1, 2, 0, 12, 1),
        new AnimationFragment(2, 5, 3, 12, 3),
        new AnimationFragment(0, 0, 0, 10, 0),
        new AnimationFragment(3, 4, 1, 8, 2),
        new AnimationFragment(1, 2, 0, 8, 1),
        new AnimationFragment(2, 5, 3, 8, 3)
    });

    // *********************************************************************
    // Number 6
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number6 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(2, 5, 0, 16, 1),
        new AnimationFragment(5, 1, 2, 16, 1),
        new AnimationFragment(6, 6, 0, 15, 3),
        new AnimationFragment(6, 6, 4, 16, 3),
        new AnimationFragment(5, 1, 4, 14, 0),
        new AnimationFragment(3, 4, 1, 12, 2),
        new AnimationFragment(2, 5, 0, 13, 2),
        new AnimationFragment(3, 4, 2, 11, 0),
        new AnimationFragment(0, 0, 0, 10, 0),
        new AnimationFragment(3, 4, 1, 8, 0),
        new AnimationFragment(1, 2, 0, 8, 1),
        new AnimationFragment(2, 5, 3, 8, 3)
    });

    // *********************************************************************
    // Number 7
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number7 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(0, 0, 4, 16, 0),
        new AnimationFragment(1, 2, 4, 14, 0),
        new AnimationFragment(3, 4, 5, 13, 1),
        new AnimationFragment(2, 5, 4, 11, 2),
        new AnimationFragment(3, 4, 1, 8, 2),
        new AnimationFragment(2, 5, 3, 8, 3),
        new AnimationFragment(1, 2, 0, 8, 1)
    });

    // *********************************************************************
    // Number 8
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number8 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(3, 4, 1, 16, 0),
        new AnimationFragment(6, 6, 0, 16, 1),
        new AnimationFragment(3, 4, 5, 16, 1),
        new AnimationFragment(1, 2, 2, 15, 3),
        new AnimationFragment(4, 7, 0, 14, 0),
        new AnimationFragment(1, 2, 1, 12, 3),
        new AnimationFragment(6, 6, 4, 13, 1),
        new AnimationFragment(2, 5, 0, 11, 1),
        new AnimationFragment(4, 7, 0, 10, 0),
        new AnimationFragment(4, 7, 4, 11, 0),
        new AnimationFragment(5, 1, 0, 8, 1),
        new AnimationFragment(5, 1, 2, 8, 1),
        new AnimationFragment(1, 2, 4, 9, 2)
    });

    // *********************************************************************
    // Number 9
    // *********************************************************************
    private static readonly IList<AnimationFragment> Number9 = new ReadOnlyCollection<AnimationFragment>
    (new[]
    {
        new AnimationFragment(0, 0, 0, 16, 0),
        new AnimationFragment(3, 4, 2, 16, 0),
        new AnimationFragment(1, 2, 2, 15, 3),
        new AnimationFragment(1, 2, 4, 15, 2),
        new AnimationFragment(3, 4, 1, 12, 2),
        new AnimationFragment(3, 4, 5, 12, 3),
        new AnimationFragment(5, 1, 0, 12, 0),
        new AnimationFragment(1, 2, 2, 11, 3),
        new AnimationFragment(5, 1, 4, 9, 0),
        new AnimationFragment(6, 6, 0, 10, 1),
        new AnimationFragment(5, 1, 0, 8, 1),
        new AnimationFragment(6, 6, 2, 8, 2)
    });

    public static int[] BlocksPerNumber =
    {
        Number0.Count, Number1.Count, Number2.Count, Number3.Count, Number4.Count, Number5.Count, Number6.Count,
        Number7.Count, Number8.Count, Number9.Count
    };

    // *********************************************************************
    // Helper function that returns the falling instruction for a given number
    // *********************************************************************
    internal static AnimationFragment GetAnimationFragment(int number, int blockIndex)
    {
        return number switch
        {
            0 => Number0[blockIndex],
            1 => Number1[blockIndex],
            2 => Number2[blockIndex],
            3 => Number3[blockIndex],
            4 => Number4[blockIndex],
            5 => Number5[blockIndex],
            6 => Number6[blockIndex],
            7 => Number7[blockIndex],
            8 => Number8[blockIndex],
            9 => Number9[blockIndex],
            _ => new AnimationFragment(0, 0, 0, 0, 0)
        };
    }

    internal readonly struct AnimationFragment
    {
        internal AnimationFragment(int blockType, int color, int xPos, int yStop, int numRot)
        {
            BlockType = blockType; // Number of the block type
            Color = color; // Color of the brick
            XPos = xPos; // x-position (starting from the left number staring point) where the brick should be placed
            YStop = yStop; // y-position (1-16, where 16 is the last line of the matrix) where the brick should stop falling
            NumRot = numRot; // Number of 90-degree (clockwise) rotations a brick is turned from the standard position
        }

        internal readonly int BlockType;
        internal readonly int Color;
        internal readonly int XPos;
        internal readonly int YStop;
        internal readonly int NumRot;
    }
}