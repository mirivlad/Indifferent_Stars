"""Original freighter, escort and combat silhouettes for the first fleet roster."""
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_ship_professions import build_variant, ship


def engine(x, y, z, radius, name):
    ship.ring("Frame", x, y, z, radius, radius * .70, .23, sides=24)
    ship.beam("Emitter", (x,y,z-.28), (x,y,z-.12), radius*.64, 24)
    ship.marker("MainNozzle_" + name, (x,y,z+.18), (0,0,1))


def slab(material, corners, lower, upper):
    winding = sum(x1*z2 - x2*z1 for (x1,z1),(x2,z2) in zip(corners, corners[1:] + corners[:1]))
    if winding > 0:
        corners = corners[::-1]
    front = [(x, upper, z) for x,z in corners]
    back = [(x, lower, z) for x,z in corners]
    ship.face(material, front)
    ship.face(material, back[::-1])
    for i in range(len(front)):
        j = (i+1) % len(front)
        ship.face(material, [front[j], front[i], back[i], back[j]])


def signal(side, point):
    ship.box("PortLight" if side < 0 else "StarboardLight", point, (.28,.16,.33))


def gannet():
    """Distributed cargo loads surround a continuous structural keel."""
    ship.loft("Frame", [(-20.2, 2.0,.93,-1.12),(-16.6,3.1,1.27,-1.06),
        (13.5,3.1,1.27,-1.06),(18.9,2.0,.85,-1.12)])
    ship.loft("Cream", [(-21,1.29,.73,.22),(-19.2,3.13,1.34,.69),
        (-15.25,3.45,1.53,.71),(-12.8,2.73,1.30,.58)])
    ship.loft("Glass", [(-20.31,1.36,.38,1.21),(-18.67,2.38,.54,1.84),(-17.55,2.73,.50,2.12)])
    ship.loft("Cream", [(-18.98,2.09,.10,2.40),(-17.61,2.73,.21,2.61),(-15.08,2.83,.17,2.37)])
    ship.marker("PilotEye", (0,1.98,-18.94))
    for side in (-1,1):
        for bay, z in enumerate((-8.2,2.0,11.6)):
            x = side * 3.79
            ship.loft("Bronze" if bay == 1 else "Cream",
                [(z-4.28,1.47,1.45,.49),(z-3.48,2.46,2.17,.49),
                 (z+3.48,2.46,2.17,.49),(z+4.28,1.47,1.45,.49)],x=x)
            for end in (-1,1):
                ship.ring("Frame",x,.49,z+end*3.44,2.53,2.39,.25,sides=24)
                ship.box("Safety",(side*4.77,2.78,z+end*3.43),(1.42,.25,.48))
                ship.beam("Frame",(side*1.85,-1.21,z+end*2.91),(side*5.06,-1.51,z+end*3.39),.27,8)
            ship.box("Frame",(side*6.26,.35,z),(.28,1.15,2.83))
            ship.box("Cream",(side*6.42,.45,z),(.11,.71,1.73))
        for j, x0 in enumerate((3.56,6.12)):
            x=side*x0
            ship.loft("Frame",[(15.19,1.09,1.07,-.35),(17.2,1.43,1.26,-.42),
                (21.16,1.29,1.17,-.42),(22.28,1.07,1.01,-.42)],x=x)
            engine(x,-.42,22.29,.98,f"{side+2}_{j}")
        ship.beam("Frame",(side*2.2,-1.02,16.1),(side*6.10,-.47,18.2),.61,12)
        ship.marker(f"Maneuver_tail_{side+2}",(side*7.62,-.42,18.37),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(side*6.07,-1.77,19.66),(0,-1,0))
        for z in (-14.9,-12.76,15.39):
            ship.box("Thermal",(side*2.65,1.72,z),(1.55,.21,1.02))
        signal(side,(side*3.31,1.54,-17.61))
    ship.marker("DockMount",(0,-2.67,-6.33))
    ship.marker("EquipmentMount_crane",(0,2.02,8.94))
    return [("G A N N E T",(0,2.83,-16.2),.60)]


def swift():
    """A single drive in a narrow lifting body with cropped delta fins."""
    ship.loft("Teal",[(-11.2,.29,.27,-.10),(-8.6,.82,.53,-.01),
        (-5.3,1.30,.84,.17),(3.6,1.66,1.09,.01),(7.45,1.19,.89,.02),
        (8.26,.87,.79,.02)])
    ship.loft("Glass",[(-8.63,.42,.17,.51),(-6.24,.78,.44,.98),(-4.49,.84,.50,1.20)])
    ship.loft("Cream",[(-6.42,.69,.089,1.49),(-4.48,.86,.14,1.68),(-3.0,.93,.21,1.21)])
    ship.marker("PilotEye",(0,1.22,-6.70))
    engine(0,.02,8.28,.80,"centre")
    for side in (-1,1):
        poly=[(side*x,z) for x,z in ((.88,-5.83),(5.68,1.7),(5.03,4.04),(1.42,5.21))]
        slab("Frame", poly if side>0 else poly[::-1], -.36,.14)
        poly=[(side*x,z) for x,z in ((1.31,-3.67),(5.13,1.90),(4.73,3.31),(1.74,3.24))]
        slab("Cream",poly if side>0 else poly[::-1], .14,.23)
        ship.beam("Teal",(side*1.16,-.14,-4.12),(side*4.96,-.12,3.11),.13,10)
        ship.loft("Frame",[(.5,.35,.31,-.52),(2.27,.57,.44,-.48),(5.69,.43,.32,-.46)], x=side*2.46)
        ship.box("Thermal",(side*2.60,.21,2.41),(.85,.22,1.55))
        signal(side,(side*5.09,.26,2.14))
        ship.marker(f"Maneuver_tip_{side+2}",(side*5.74,-.11,1.83),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(side*2.42,-.99,2.96),(0,-1,0))
        ship.marker(f"EquipmentMount_{side+2}",(side*1.47,-.71,-2.17))
    ship.marker("DockMount",(0,-1.04,4.29))
    return [("S W I F T",(0,1.35,-3.63),.34)]


def merlin():
    """A short escort hull with lateral propulsion beams and a centreline deck."""
    ship.loft("Frame",[(-15.3,.94,.55,-.25),(-12.4,2.3,1.36,-.12),
        (-3.8,3.1,1.86,-.16),(10.1,2.87,1.44,-.46),(13.4,1.84,1.17,-.37)])
    ship.loft("Cream",[(-15.8,.74,.46,-.05),(-13.22,2.01,.80,.44),(-5.8,2.54,1.21,.64),
        (1.9,2.33,1.26,.61),(8.5,1.69,.70,.12)])
    ship.loft("Glass",[(-11.68,.93,.42,1.68),(-9.96,1.70,.54,2.21),(-8.10,1.93,.55,2.45)])
    ship.loft("Cream",[(-10.23,1.54,.103,2.73),(-8.00,1.98,.17,2.96),(-5.89,1.78,.23,2.61)])
    ship.marker("PilotEye",(0,2.49,-10.02))
    for side in (-1,1):
        x=side*4.24
        ship.loft("Teal",[(-7.79,.74,.52,-.42),(-4.23,1.36,1.13,-.44),
            (8.7,1.61,1.13,-.57),(13.4,1.42,.98,-.57),(14.4,1.01,.86,-.57)],x=x)
        engine(x,-.57,14.41,.82,str(side+2))
        for z in (-4.9,7.48):
            ship.beam("Frame",(side*2.20,-.65,z+.57),(x,-.42,z),.43,10)
            ship.beam("Frame",(side*2.22,-1.07,z+.82),(x,-.98,z+2),.21)
        ship.loft("Cream",[(-2.87,.71,.26,.63),(-.9,1.00,.31,.82),(3.05,1.15,.35,.82),(5.1,.81,.21,.68)],x=x)
        for z in (-2.1,6.39,8.53):
            ship.box("Thermal",(x,.73,z),(1.46,.18,1.19))
        signal(side,(side*5.49,.28,-4.53))
        ship.marker(f"Maneuver_side_{side+2}",(side*5.96,-.43,7.81),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(x,-1.79,9.41),(0,-1,0))
        ship.marker(f"EquipmentMount_escort_{side+2}",(side*2.48,.90,-3.80))
    ship.beam("Frame",(0,1.66,6.72),(0,3.27,7.11),.12)
    ship.beam("Cream",(-1.25,3.20,7.11),(1.25,3.20,7.11),.13)
    ship.marker("DockMount",(0,-2.14,-.27))
    return [("M E R L I N",(0,1.88,-1.05),.51)]


def shrike():
    """An asymmetrically reinforced salvage hull with a salvaged drive outrigger."""
    ship.loft("Bronze",[(-12.38,1.03,.32,-.50),(-10.58,2.17,.99,-.16),
        (-4.15,2.51,1.12,-.03),(5.59,2.66,1.41,-.39),(9.9,1.39,.94,-.58)])
    ship.loft("Frame",[(-10.8,.87,.61,.56),(-8.8,1.62,.92,.88),
        (-5.8,1.83,1.10,1.30),(-3.94,1.41,.69,.96)],x=.73)
    ship.loft("Glass",[(-9.83,.69,.48,1.33),(-8.21,1.25,.47,1.79),(-6.78,1.42,.45,2.04)],x=.73)
    ship.loft("Bronze",[(-8.38,1.27,.081,2.28),(-6.72,1.48,.13,2.54),(-5.61,1.51,.13,2.27)],x=.73)
    ship.marker("PilotEye",(.73,1.83,-8.71))
    ship.loft("Frame",[(-5.13,1.13,1.03,-.53),(-2.21,1.69,1.41,-.49),
        (7.69,1.64,1.46,-.51),(11.28,1.30,1.15,-.51)],x=-3.50)
    engine(-3.50,-.51,11.33,1.05,"salvaged")
    ship.ring("Safety",-3.50,-.49,5.71,1.65,1.53,.37)
    ship.beam("Frame",(-1.98,-.47,-1.59),(-3.51,-.63,-1.76),.43,10)
    ship.beam("Frame",(-1.33,-.51,5.18),(-3.46,-.44,5.07),.37,10)
    ship.loft("Frame",[(4.23,.83,.56,-.56),(8.22,1.14,.96,-.53),(11.32,.91,.77,-.53)],x=.71)
    engine(.71,-.53,11.29,.70,"centre")
    slab("Frame",[(2.06,-7.98),(5.41,-4.85),(5.25,1.79),(3.24,6.29),(1.93,5.95)],-.47,.39)
    slab("Bronze",[(2.22,-7.33),(5.10,-4.54),(4.83,1.61),(3.21,5.61),(2.20,5.01)],.39,.51)
    for z in (-4.07,-.39,3.27):
        ship.box("Cream",(3.69,.52,z),(1.43,.16,.47))
    ship.beam("Frame",(4.77,.36,-3.30),(4.69,.58,1.43),.13)
    ship.box("Thermal",(-2.84,.98,3.43),(1.52,.21,2.32))
    signal(-1,(-4.94,.14,1.92))
    signal(1,(5.09,.65,-4.28))
    ship.marker("Maneuver_outboard",(-5.24,-.48,7.67),(-1,0,0))
    ship.marker("Maneuver_armour",(5.48,-.06,-4.77),(1,0,0))
    ship.marker("Maneuver_keel",(1.03,-1.63,8.42),(0,-1,0))
    ship.marker("Maneuver_fore",(1.12,-.19,-12.38),(0,0,-1))
    ship.marker("EquipmentMount_salvage",(-2.47,.44,-3.68))
    ship.marker("EquipmentMount_raid",(3.34,.62,-6.04))
    ship.marker("DockMount",(0,-1.85,4.16))
    return [("S / 3",(3.69,.613,.41),.39)]


def halberd():
    """An armoured keel with an elevated bridge, a split forward spine and paired drive decks."""
    ship.loft("Frame",[(-19.1,1.63,1.15,-.75),(-15.78,3.42,2.28,-.68),
        (-1.33,4.60,2.84,-.52),(13.4,3.83,2.75,-.66),(19.74,2.66,1.94,-.58)])
    ship.loft("Teal",[(-16.76,1.33,.76,1.23),(-11.0,2.54,1.68,1.69),
        (10.84,2.93,1.86,1.48),(16.65,2.02,1.28,1.08)])
    ship.loft("Cream",[(-9.9,1.43,.53,3.37),(-7.34,2.20,1.30,3.87),
        (-2.17,2.61,1.45,3.96),(2.71,2.47,1.12,3.40)])
    ship.loft("Glass",[(-9.48,1.25,.52,3.85),(-7.96,1.97,.69,4.51),(-6.02,2.43,.62,4.91)])
    ship.loft("Cream",[(-8.09,1.82,.09,5.25),(-5.89,2.44,.20,5.52),(-3.24,2.41,.16,5.17)])
    ship.marker("PilotEye",(0,4.61,-8.18))
    for side in (-1,1):
        x=side*4.31
        ship.loft("Teal",[(-21.40,.42,.35,-.22),(-19.45,1.08,1.23,-.17),
            (-11.36,1.65,1.87,-.25),(0.64,1.83,2.05,-.43),(5.24,1.18,1.21,-.45)],x=x)
        ship.loft("Cream",[(-20.21,.75,.31,.66),(-17.48,1.33,.34,1.29),
            (-11.35,1.38,.39,1.63),(-8.2,1.12,.32,1.43)],x=x)
        for z in (-14.43,-7.41,.42):
            ship.beam("Frame",(side*2.78,-.63,z+1.36),(x,-.47,z),.53,12)
        for lane in (-1,1):
            y=lane*1.50-.56
            ship.loft("Frame",[(8.2,1.01,.74,y),(11.35,1.91,1.23,y),
                (19.9,1.73,1.17,y),(22.31,1.20,.99,y)],x=x)
            engine(x,y,22.35,.97,f"{side+2}_{lane+2}")
        for z in (3.34,6.55,8.64):
            ship.box("Thermal",(side*2.69,3.28,z),(1.23,.24,1.89))
        signal(side,(side*5.82,.76,-8.69))
        ship.marker(f"Maneuver_side_{side+2}",(side*6.38,.86,12.22),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(x,-3.43,11.83),(0,-1,0))
        ship.marker(f"EquipmentMount_main_{side+2}",(side*3.97,1.93,-11.38))
        ship.marker(f"EquipmentMount_quarter_{side+2}",(side*4.03,2.34,11.51))
    ship.marker("DockMount",(0,-3.46,-.36))
    return [("H A L B E R D",(0,3.42,4.15),.65)]


SHAPES = {shape.__name__:shape for shape in (gannet, swift, merlin, shrike, halberd)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("key", choices=SHAPES)
    variant = parser.parse_args(sys.argv[sys.argv.index("--") + 1:]).key
    build_variant(variant, SHAPES[variant], __file__)
