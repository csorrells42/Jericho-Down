namespace JerichoDown.Modules.Midi;

public sealed class MidiControlMappingTriggerState
{
    private readonly HashSet<MidiControlMappingRule> _activeMomentaryMappings = [];

    public IReadOnlyList<MidiControlMappingRule> GetTriggeredMappings(
        IEnumerable<MidiControlMappingRule> mappings,
        MidiMessageSnapshot message)
    {
        foreach (var mapping in mappings)
        {
            if (mapping.IsRelease(message))
            {
                _activeMomentaryMappings.Remove(mapping);
            }
        }

        var triggered = new List<MidiControlMappingRule>();
        foreach (var mapping in mappings)
        {
            if (!mapping.ShouldTrigger(message))
            {
                continue;
            }

            if (mapping.IsMomentary && !_activeMomentaryMappings.Add(mapping))
            {
                continue;
            }

            triggered.Add(mapping);
        }

        return triggered;
    }

    public void Remove(MidiControlMappingRule mapping)
    {
        _activeMomentaryMappings.Remove(mapping);
    }

    public void Clear()
    {
        _activeMomentaryMappings.Clear();
    }
}
