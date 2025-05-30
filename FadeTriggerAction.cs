using Microsoft.Maui.Controls;

namespace AviationApp;

public class FadeTriggerAction : TriggerAction<VisualElement>
{
    public int Duration { get; set; } = 500;
    public double TargetOpacity { get; set; } = 1.0;

    protected override async void Invoke(VisualElement sender)
    {
        await sender.FadeTo(TargetOpacity, (uint)Duration, Easing.SinInOut);
    }
}