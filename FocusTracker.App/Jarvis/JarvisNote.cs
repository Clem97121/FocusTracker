using System;

namespace FocusTracker.App.Jarvis
{
    // Повідомлення асистента, яке показується в кутку екрана
    public record JarvisNote(DateTime At, string Kind, string Text);
}
