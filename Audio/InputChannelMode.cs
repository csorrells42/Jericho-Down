namespace JerichoDown.Audio;

public enum InputChannelMode
{
    MonoSum,
    Input1Left,
    Input2Right,
    Input3,
    Input4,
    Input5,
    Input6,
    Input7,
    Input8,
    Input9,
    Input10
}

public static class InputChannelModeInfo
{
    public static InputChannelMode? GetChannelMode(int channelIndex)
    {
        return channelIndex switch
        {
            0 => InputChannelMode.Input1Left,
            1 => InputChannelMode.Input2Right,
            2 => InputChannelMode.Input3,
            3 => InputChannelMode.Input4,
            4 => InputChannelMode.Input5,
            5 => InputChannelMode.Input6,
            6 => InputChannelMode.Input7,
            7 => InputChannelMode.Input8,
            8 => InputChannelMode.Input9,
            9 => InputChannelMode.Input10,
            _ => null
        };
    }

    public static int? GetSelectedChannelIndex(InputChannelMode mode)
    {
        return mode switch
        {
            InputChannelMode.Input1Left => 0,
            InputChannelMode.Input2Right => 1,
            InputChannelMode.Input3 => 2,
            InputChannelMode.Input4 => 3,
            InputChannelMode.Input5 => 4,
            InputChannelMode.Input6 => 5,
            InputChannelMode.Input7 => 6,
            InputChannelMode.Input8 => 7,
            InputChannelMode.Input9 => 8,
            InputChannelMode.Input10 => 9,
            _ => null
        };
    }

    public static string GetDisplayLabel(InputChannelMode mode)
    {
        return mode switch
        {
            InputChannelMode.MonoSum => "Mono sum",
            InputChannelMode.Input1Left => "Input 1 L",
            InputChannelMode.Input2Right => "Input 2 R",
            InputChannelMode.Input3 => "Input 3",
            InputChannelMode.Input4 => "Input 4",
            InputChannelMode.Input5 => "Input 5",
            InputChannelMode.Input6 => "Input 6",
            InputChannelMode.Input7 => "Input 7",
            InputChannelMode.Input8 => "Input 8",
            InputChannelMode.Input9 => "Input 9",
            InputChannelMode.Input10 => "Input 10",
            _ => mode.ToString()
        };
    }
}


