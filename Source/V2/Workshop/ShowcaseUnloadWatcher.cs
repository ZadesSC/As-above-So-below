#if RIMWORLD_17
#nullable enable annotations
using UnityEngine;

namespace AsAboveSoBelow;

public class ShowcaseUnloadWatcher : MonoBehaviour
{
    private void LateUpdate()
    {
        Showcase.LateFrame();
    }

    private void OnDestroy()
    {
        Showcase.WatcherDestroyed(this);
    }
}
#endif
