using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reuses short-lived effect objects (tracers, muzzle flashes, blood, sounds) instead of creating and destroying
/// a new GameObject for every shot. Objects are parked switched off and handed out again by kind.
/// </summary>
public class EffectPool : MonoBehaviour
{
    struct Timed
    {
        public GameObject Object;
        public string Kind;
        public float Until;
    }

    static EffectPool instance;
    readonly Dictionary<string, Stack<GameObject>> free = new Dictionary<string, Stack<GameObject>>();
    readonly List<Timed> live = new List<Timed>();

    static EffectPool Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("Effect Pool").AddComponent<EffectPool>();
            return instance;
        }
    }

    /// <summary>A parked object of this kind (switched on), or a new one from <paramref name="create"/>.</summary>
    public static GameObject Take(string kind, Func<GameObject> create)
    {
        if (Instance.free.TryGetValue(kind, out var stack))
        {
            while (stack.Count > 0)
            {
                var parked = stack.Pop();
                if (parked == null) continue;
                parked.SetActive(true);
                return parked;
            }
        }
        return create();
    }

    public static void Return(GameObject effect, string kind)
    {
        if (effect == null) return;
        effect.SetActive(false);
        if (!Instance.free.TryGetValue(kind, out var stack)) Instance.free[kind] = stack = new Stack<GameObject>();
        stack.Push(effect);
    }

    public static void ReturnAfter(GameObject effect, string kind, float seconds) =>
        Instance.live.Add(new Timed { Object = effect, Kind = kind, Until = Time.time + seconds });

    void Update()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (Time.time < live[i].Until) continue;
            Return(live[i].Object, live[i].Kind);
            live[i] = live[live.Count - 1];
            live.RemoveAt(live.Count - 1);
        }
    }
}
