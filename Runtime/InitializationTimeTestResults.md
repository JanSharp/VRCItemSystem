
inital one with 1000 Starts doing stuff
```
2022.06.07 23:47:53 Log        -  Loading asset bundle: JanSync
2022.06.08 00:46:31 Log        -  [Behaviour] Room instantiate took 14.94529s
2022.06.07 23:48:15 Log        -  [Behaviour] Finished entering world.
total: 22s
```

second one still with 1000 Starts only registering for debug
```
2022.06.08 00:28:13 Log        -  Loading asset bundle: ItemSync
2022.06.08 00:46:31 Log        -  [Behaviour] Room instantiate took 15.22827s
2022.06.08 00:28:32 Log        -  [Behaviour] Finished entering world.
total: 19s
```

third one, no Starts, no debug
```
2022.06.08 00:46:18 Log        -  Loading asset bundle: ItemSync
2022.06.08 00:46:31 Log        -  [Behaviour] Room instantiate took 13.14624s
2022.06.08 00:46:35 Log        -  [Behaviour] Finished entering world.
total: 17s
```

ok, wow, not impressive, let me try again just to see how consistent the numbers are
```
2022.06.08 00:49:50 Log        -  Loading asset bundle: ItemSync
2022.06.08 00:50:03 Log        -  [Behaviour] Room instantiate took 13.58569s
2022.06.08 00:50:08 Log        -  [Behaviour] Finished entering world.
total: 18s
```

now I want to know how long it takes with 1000 disabled items
```
2022.06.08 00:54:35 Log        -  Loading asset bundle: ItemSync
2022.06.08 00:54:48 Log        -  [Behaviour] Room instantiate took 13.1377s
2022.06.08 00:54:51 Log        -  [Behaviour] Finished entering world.
total: 16s
```

alright, tbh I'm not surprised
but what does this really mean?
it means that having thousands of udonbehaviours in a world makes VRChat take forever to load the map
that's what it means
even when they don't have a Start of OnAwake method/event
so, we have a few options... well maybe, i need to think
ok first of all, removing the Start method did shave off around 5 seconds, which is at least something
but before I go off comming to conclusions let's test a world with just a few udon behaviours
the same world to be exact, but without the 1000 items
```
2022.06.08 01:05:03 Log        -  Loading asset bundle: ItemSync
2022.06.08 01:05:04 Log        -  [Behaviour] Room instantiate took 1.244629s
2022.06.08 01:05:08 Log        -  [Behaviour] Finished entering world.
total: 5s
```

this is such a short amount of time, I need more samples
```
2022.06.08 01:06:47 Log        -  Loading asset bundle: ItemSync
2022.06.08 01:06:48 Log        -  [Behaviour] Room instantiate took 1.280762s
2022.06.08 01:06:52 Log        -  [Behaviour] Finished entering world.
total: 5s
```

alright, I'm all for custom object pools that don't have any items to start with (or probably just 1 to start with) that instantiate more as they are needed, but reuse if there are some already instantiated ones that are currently available (meaning disabled).
There is no way instantiating the currently used itmes when loading it takes longer than 20 seconds, which is how long it would VRChat to initialize 2000 items with item syncs on them where the vast majority of them are disabled
