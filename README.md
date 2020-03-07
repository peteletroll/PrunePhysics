# PrunePhysics

Highly experimental - backup your savefiles!

This module can turn normal parts into pysicsless parts, hopefully making
possible to build larger ships.

The module contains a whitelist; parts containing modules
or resources not in the whitelist can not be converted to physicsless.
The whitelist is contained in files named \*.prunephysicswhitelist or
\*.ppwl, placed anywhere in the GameData folder. Any mod can add itself
to the PrunePhysics whitelist by adding a whitelist file to its own
distribution.

To convert a part to physicsless, activate the PrunePhysics switch
in the PAW, and force a reload of the craft by F5-F9, or by switching to
a far away craft and back, or by going to the KSC and back.

You can reverse this by disabling the PrunePhysics switch and forcing
a craft reload again.

If the craft has not been reloaded yet, a "WAIT" warning will appear in
the PAW.

The physicsless conversion is extended to all the symmetry counterparts.

