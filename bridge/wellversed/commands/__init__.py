"""WellVersed command modules.

Importing this package auto-registers every command module so the bridge
listener can dispatch to them.
"""

from . import (  # noqa: F401
    actors,
    world_state,
    stamps,
    geometry,
    zones,
    verse,
    materials,
    environment,
    publish,
    viewport,
    plan_executor,
    api_crawler,
    splines,
    sim_device_proxy,
    prop_patterns,
    foliage_tools,
    lighting,
    audio,
    organization,
    optimization,
    sequencer,
    selection,
    measurement,
)
