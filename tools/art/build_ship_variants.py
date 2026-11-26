"""Second original airframe for each fleet class that has two planned slots."""
import argparse
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_ship_specialists as forms
from build_ship_professions import build_variant, ship


def plank(material, points, lower, upper):
    forms.slab(material, points, lower, upper)


def nozzle(x, y, z, radius, label):
    forms.engine(x, y, z, radius, label)


def pipit():
    # Forward pilot capsule on a triangular bridge above an open aft frame.
    ship.loft("Cream",[(-9.3,.63,.32,.39),(-7.6,1.42,.71,.45),(-3.98,1.65,.85,.47),(-1.3,.89,.51,.34)])
    ship.loft("Glass",[(-8.63,.71,.22,.94),(-7.23,1.08,.44,1.16),(-5.65,1.43,.43,1.44)])
    ship.loft("Teal",[(-7.39,1.02,.076,1.64),(-5.57,1.37,.15,1.92),(-3.94,1.30,.10,1.65)])
    ship.marker("PilotEye",(0,1.33,-7.61))
    ship.loft("Frame",[(-5.3,.64,.49,-.44),(-2.13,1.71,.58,-.53),(.27,1.58,.57,-.50)])
    for side in (-1,1):
        x=side*2.30
        ship.beam("Frame",(side*.56,-.46,-3.37),(x,-.22,-.33),.36,10)
        ship.loft("Teal",[(-1.8,.46,.50,-.33),(.27,.73,.72,-.30),(6.0,.83,.74,-.36),(7.35,.57,.58,-.34)],x=x)
        nozzle(x,-.34,7.42,.50,str(side+2))
        ship.beam("Frame",(x,-.41,4.8),(side*.55,-.84,3.92),.16)
        ship.box("Thermal",(x,.45,2.91),(.81,.20,1.92))
        ship.marker(f"Maneuver_side_{side+2}",(side*3.17,-.29,4.39),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(x,-1.12,3.06),(0,-1,0))
        forms.signal(side,(x,.48,-.11))
    ship.box("Cream",(0,-.71,3.93),(1.23,.46,.95))
    ship.marker("DockMount",(0,-1.16,3.84))
    return [("P I P I T",(0,1.80,-4.59),.34)]


def skua():
    # Raised freight saddle with two detachable ventral bins and a central tug head.
    ship.loft("Frame",[(-10.3,1.05,.68,.40),(-7.1,1.67,.84,.80),(6.82,1.41,.72,.92),(11.6,1.13,.78,.69)])
    ship.loft("Bronze",[(-11.05,.71,.61,.66),(-9.36,1.66,.82,1.03),(-5.56,1.85,1.04,1.10),(-3.84,1.32,.78,.97)])
    ship.loft("Glass",[(-10.51,.79,.35,1.35),(-9.33,1.30,.49,1.77),(-7.69,1.52,.49,1.97)])
    ship.loft("Cream",[(-9.48,1.21,.068,2.34),(-7.52,1.47,.14,2.52),(-5.82,1.43,.17,2.25)])
    ship.marker("PilotEye",(0,1.92,-9.42))
    for side in (-1,1):
        x=side*2.67
        ship.loft("Cream",[(-4.51,1.29,.81,-.98),(-3.4,1.68,1.08,-.94),(5.29,1.65,1.12,-.94),(6.39,1.17,.80,-.94)],x=x)
        for z in (-3.65,1.0,5.47):
            ship.beam("Frame",(side*.89,.94,z+.34),(side*3.18,.21,z),.25)
            ship.box("Safety",(side*3.15,.25,z),(.71,.21,.42))
        ship.beam("Frame",(side*3.81,-1.77,-3.52),(side*3.81,-1.77,5.08),.16)
        ship.loft("Bronze",[(5.06,.59,.63,.85),(6.24,.98,.88,.83),(10.62,.91,.87,.66),(11.58,.68,.64,.64)],x=side*2.47)
        nozzle(side*2.47,.64,11.65,.61,str(side+2))
        ship.box("Thermal",(side*2.49,1.60,8.1),(1.11,.21,2.02))
        ship.marker(f"Maneuver_side_{side+2}",(side*4.42,-.91,1.66),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(side*2.42,-2.14,3.39),(0,-1,0))
        forms.signal(side,(side*3.97,.06,-2.91))
    ship.marker("DockMount",(0,-1.23,-7.75))
    ship.marker("EquipmentMount_tow",(0,-.81,-9.75))
    return [("S K U A",(0,2.59,-7.1),.47)]


def cairn():
    # A single pressure vessel inside a tetrahedral frame with distributed pushers.
    ship.loft("Frame",[(-20.5,1.09,.73,-.27),(-18.08,2.83,1.13,-.27),(-12.9,2.65,1.39,-.50),
        (15.3,2.94,1.63,-.51),(22.42,2.10,1.37,-.36)])
    ship.loft("Cream",[(-20.82,.74,.54,.62),(-18.7,2.21,.98,1.15),(-15.84,2.27,1.21,1.44),(-13.72,1.59,.75,1.20)])
    ship.loft("Glass",[(-20.21,.88,.38,1.43),(-18.26,1.83,.56,2.17),(-16.78,2.03,.54,2.42)])
    ship.loft("Cream",[(-18.52,1.67,.12,2.78),(-16.67,1.99,.18,2.97),(-14.70,1.89,.15,2.71)])
    ship.marker("PilotEye",(0,2.18,-18.72))
    ship.beam("Bronze",(0,1.00,-9.97),(0,1.00,15.13),3.64,24)
    ship.loft("Cream",[(-12.2,.8,.61,1.0),(-10.1,3.58,3.54,1.0),(-8.77,3.63,3.61,1.0)])
    ship.loft("Cream",[(13.92,3.62,3.55,1.0),(15.64,3.50,3.48,1.0),(17.66,.90,.71,1.0)])
    for z in (-7.7,-.24,7.71,13.66):
        ship.ring("Frame",0,1.0,z,3.80,3.59,.53,sides=24)
    for index in range(3):
        angle=math.tau*index/3+math.pi*.5
        x=5.28*math.cos(angle)
        y=.85+5.28*math.sin(angle)
        ship.beam("Frame",(x,y,-9.4),(x,y,16.91),.35,10)
        ship.beam("Frame",(x,y,-9.4),(x*.38,.45+y*.16,-14.2),.34,10)
        for z in (-6.9,1.8,10.38):
            ship.beam("Cream",(x,y,z),(x*.63,.31+y*.67,z+.3),.22)
        ship.loft("Frame",[(14.0,.98,.89,y),(17.8,1.61,1.40,y),(22.13,1.46,1.22,y),(23.94,1.12,.97,y)],x=x)
        nozzle(x,y,23.95,.91,str(index))
        normal=(math.cos(angle),math.sin(angle),0)
        ship.marker(f"Maneuver_tail_{index}",(x+normal[0]*1.79,y+normal[1]*1.79,17.94),normal)
    ship.box("Thermal",(0,6.40,6.39),(2.0,.21,2.89))
    forms.signal(-1,(-3.95,.07,-7.65))
    forms.signal(1,(3.95,.07,-7.65))
    ship.marker("DockMount",(0,-3.11,-10.33))
    ship.marker("EquipmentMount_pump",(0,2.98,-14.27))
    return [("C A I R N",(0,3.07,-16.15),.62)]


def dredge():
    # Ventral rotating cutter and a raised bridge with an open work area beneath.
    ship.loft("Frame",[(-10.2,.60,.60,-.21),(-7.42,1.95,1.02,-.02),(-1.33,2.32,1.31,.09),
        (7.98,2.26,1.43,-.32),(10.72,1.62,.89,-.44)])
    ship.loft("Bronze",[(-7.71,1.53,.83,.47),(-4.82,2.23,1.29,.65),(5.2,2.19,1.37,.56),(8.11,1.63,1.18,.46)])
    ship.loft("Cream",[(-8.15,.66,.30,1.48),(-6.32,1.44,.79,1.65),(-4.06,1.56,1.19,1.70),(-2.31,1.38,.81,1.61)])
    ship.loft("Glass",[(-7.7,.71,.34,1.98),(-6.15,1.27,.53,2.41),(-4.87,1.45,.50,2.69)])
    ship.loft("Cream",[(-6.29,1.22,.077,2.95),(-4.7,1.46,.15,3.20),(-3.31,1.41,.19,2.94)])
    ship.marker("PilotEye",(0,2.51,-6.46))
    ship.ring("Frame",0,-2.08,-4.84,2.28,1.70,1.37,sides=36)
    ship.ring("Safety",0,-2.08,-5.60,2.34,1.82,.21,sides=36)
    for i in range(12):
        angle=i*math.tau/12
        x,y=2.11*math.cos(angle),2.11*math.sin(angle)
        ship.beam("Frame",(x,y-2.08,-5.64),(x*.74,y*.74-2.08,-6.11),.14,8)
    ship.marker("EquipmentMount_cutter",(0,-2.08,-6.04))
    for side in (-1,1):
        x=side*2.88
        ship.beam("Frame",(side*1.39,-1.02,-1.8),(side*1.93,-2.14,-4.88),.41,12)
        ship.beam("Cream",(side*1.52,-.69,1.6),(x,-.37,4.83),.48,10)
        ship.loft("Bronze",[(3.22,.69,.71,-.53),(5.78,1.35,1.14,-.63),(10.25,1.19,.98,-.59),(11.52,.92,.83,-.59)],x=x)
        nozzle(x,-.59,11.57,.77,str(side+2))
        ship.box("Thermal",(x,.73,7.48),(1.28,.23,2.08))
        ship.marker(f"Maneuver_side_{side+2}",(side*4.31,-.54,7.89),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(x,-1.83,7.02),(0,-1,0))
        forms.signal(side,(side*2.17,.69,-4.74))
    ship.marker("DockMount",(0,-1.80,6.65))
    return [("D R E D G E",(0,3.35,-3.57),.36)]


def ibis():
    # Pairs of sensors are spaced by a closed loop behind the piloting capsule.
    ship.loft("Cream",[(-8.54,.49,.43,.32),(-6.27,1.52,.79,.60),(-3.12,1.61,1.15,.78),
        (.17,1.47,.96,.66),(3.31,.84,.55,.59)],x=-.62)
    ship.loft("Glass",[(-7.63,.58,.33,.87),(-5.74,1.06,.41,1.30),(-4.05,1.42,.48,1.71)],x=-.62)
    ship.loft("Teal",[(-5.9,1.05,.10,1.79),(-3.87,1.42,.17,2.17),(-2.37,1.40,.14,1.94)],x=-.62)
    ship.marker("PilotEye",(-.62,1.52,-5.96))
    ship.loft("Frame",[(-3.97,.84,.48,-.34),(1.26,1.39,.74,-.62),(11.22,1.13,.81,-.63)])
    ship.ring("Teal",0,.73,6.12,4.44,4.06,.57,sides=44)
    ship.ring("Frame",0,.73,6.27,4.08,3.87,.30,sides=44)
    for i in range(8):
        angle=i*math.tau/8
        x,y=4.13*math.cos(angle),4.13*math.sin(angle)
        ship.beam("Cream",(x,y+.73,5.84),(x*1.12,y*1.12+.73,6.21),.12,8)
        ship.box("Emitter",(x*1.07,y*1.07+.73,5.90),(.25,.23,.16))
    for side in (-1,1):
        ship.beam("Frame",(side*.89,-.47,2.84),(side*3.95,.41,6.12),.26)
        ship.loft("Teal",[(5.94,.40,.42,-.32),(8.02,.72,.71,-.51),(11.48,.75,.74,-.48)],x=side*1.59)
        nozzle(side*1.59,-.48,11.52,.67,str(side+2))
        ship.marker(f"Maneuver_side_{side+2}",(side*4.68,.78,6.20),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(side*1.59,-1.33,9.10),(0,-1,0))
        forms.signal(side,(side*2.91,3.87,5.78))
    ship.marker("EquipmentMount_interferometer",(0,5.17,6.12))
    ship.marker("DockMount",(0,-1.53,3.59))
    return [("I B I S",(-.62,2.36,-3.23),.38)]


def kestrelwing():
    # Twin forward canards come from rear engine roots and converge on the nose probe.
    ship.loft("Frame",[(-9.6,.41,.28,-.24),(-7.37,1.23,.69,-.26),(-2.9,1.42,.94,-.31),
        (4.51,1.51,.93,-.4),(7.46,1.09,.64,-.66)])
    ship.loft("Bronze",[(-10.15,.32,.20,-.22),(-6.49,1.31,.56,.30),(-3.35,1.43,.84,.43),(1.92,1.19,.48,.22)])
    ship.loft("Glass",[(-8.12,.38,.18,.59),(-6.41,.92,.38,.84),(-4.75,1.12,.44,1.20)])
    ship.loft("Cream",[(-6.57,.88,.077,1.28),(-4.59,1.09,.17,1.55),(-2.87,1.13,.13,1.34)])
    ship.marker("PilotEye",(0,1.03,-6.49))
    for side in (-1,1):
        ship.loft("Bronze",[(-7.61,.26,.28,-.23),(-5.16,.62,.43,-.37),(.26,.75,.55,-.55),
            (5.67,1.08,.84,-.62),(8.53,.79,.65,-.63)],x=side*2.56)
        nozzle(side*2.56,-.63,8.57,.66,str(side+2))
        plank("Frame",[(side*x,z) for x,z in ((.96,-2.7),(3.21,-4.9),(3.64,-3.74),(2.71,3.39),(1.15,3.41))],-.35,.05)
        ship.beam("Cream",(side*.89,-.15,-4.86),(side*2.58,-.32,-6.02),.15,8)
        ship.box("Thermal",(side*2.56,.32,5.82),(1.0,.18,1.64))
        ship.marker(f"Maneuver_side_{side+2}",(side*3.72,-.63,5.31),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(side*2.55,-1.51,5.62),(0,-1,0))
        ship.marker(f"EquipmentMount_forward_{side+2}",(side*2.56,-.26,-7.63))
        forms.signal(side,(side*3.18,-.06,-4.86))
    ship.marker("DockMount",(0,-1.32,2.47))
    return [("K / 11",(0,1.00,.19),.34)]


def osprey():
    # A raised dorsal gantry over an open catamaran lengthens the equipment baseline.
    for side in (-1,1):
        x=side*3.33
        ship.loft("Cream",[(-14.2,.51,.38,-.41),(-11.38,1.29,.92,-.27),
            (-3.28,1.48,1.12,-.18),(10.51,1.76,1.28,-.23),(14.81,1.29,.98,-.37)],x=x)
        nozzle(x,-.37,14.86,.83,str(side+2))
        for z in (-8.14,-.20,8.81):
            ship.beam("Frame",(x,-.33,z),(0,1.46,z+.71),.34,10)
        ship.loft("Teal",[(-5.61,.63,.22,.84),(-4.01,1.03,.38,1.12),(1.72,1.10,.41,1.11),
            (4.42,.61,.24,.78)],x=x)
        ship.box("Thermal",(x,1.19,7.42),(1.19,.19,2.01))
        ship.marker(f"Maneuver_side_{side+2}",(side*5.19,-.31,9.67),(side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}",(x,-1.64,9.08),(0,-1,0))
        ship.marker(f"EquipmentMount_deck_{side+2}",(x,1.57,-1.87))
        forms.signal(side,(side*4.41,.84,-9.36))
    ship.loft("Frame",[(-12.19,.59,.61,1.41),(-9.38,1.38,.87,1.65),(9.62,1.46,.84,1.81),(12.24,.89,.69,1.39)])
    ship.loft("Glass",[(-11.63,.67,.32,2.01),(-9.77,1.20,.46,2.37),(-8.11,1.47,.49,2.70)])
    ship.loft("Teal",[(-9.9,1.20,.11,2.88),(-8.0,1.48,.21,3.23),(-5.27,1.35,.17,2.78)])
    ship.marker("PilotEye",(0,2.58,-9.97))
    ship.beam("Frame",(0,2.63,7.21),(0,4.82,7.57),.14)
    ship.beam("Cream",(-1.73,4.71,7.59),(1.73,4.71,7.59),.13)
    ship.box("Teal",(0,4.79,7.51),(1.95,.26,.70))
    ship.marker("DockMount",(0,-1.70,7.48))
    return [("O S P R E Y",(0,2.73,2.9),.42)]


def scavenger():
    # An industrial rig has become a lopsided scrap carrier with a missing crescent arm.
    ship.loft("Bronze",[(-10.16,.58,.48,-.23),(-7.8,1.47,1.08,.18),
        (-3.66,2.26,1.47,.09),(6.69,2.40,1.30,-.18),(11.19,1.55,1.04,-.48)])
    ship.loft("Frame",[(-9.42,.70,.38,.96),(-7.13,1.25,.67,1.35),(-4.49,1.49,.88,1.60),(-2.71,1.20,.64,1.32)],x=-.81)
    ship.loft("Glass",[(-8.74,.57,.32,1.45),(-6.77,1.12,.43,1.94),(-5.17,1.40,.41,2.19)],x=-.81)
    ship.loft("Cream",[(-6.94,1.04,.08,2.38),(-5.07,1.39,.13,2.65),(-3.34,1.30,.17,2.40)],x=-.81)
    ship.marker("PilotEye",(-.81,2.08,-6.91))
    x=2.95
    ship.beam("Frame",(1.52,-.41,-.90),(x,1.31,2.49),.42,10)
    ship.beam("Frame",(1.89,-.47,5.06),(x,-1.12,6.4),.53,12)
    for y in (-1.13,1.08):
        ship.loft("Teal",[(1.58,.58,.68,y),(3.73,1.32,1.03,y),(10.69,1.28,1.02,y),(12.29,.94,.83,y)],x=x)
        nozzle(x,y,12.34,.80,"upper" if y>0 else "lower")
    for z in (-3.42,-1.01,2.90):
        ship.beam("Frame",(-1.58,-.33,z+.42),(-3.97,-.28,z),.28,10)
    for a,b in [
        ((-1.78,-.28,-7.34),(-3.77,-.28,-5.33)),
        ((-3.77,-.28,-5.33),(-4.75,-.28,-2.31)),
        ((-4.75,-.28,-2.31),(-4.32,-.28,.72)),
        ((-4.32,-.28,.72),(-3.05,-.28,3.48)),
        ((-3.05,-.28,3.48),(-2.13,-.28,5.02)),
    ]:
        ship.beam("Cream",a,b,.42,10)
    ship.box("Safety",(-4.29,.22,-1.54),(.64,.17,1.35))
    ship.box("Thermal",(x,2.22,7.32),(1.72,.24,2.06))
    ship.box("Bronze",(-2.26,1.15,1.93),(1.65,.27,2.52))
    ship.marker("Maneuver_outrigger",(4.37,1.02,7.81),(1,0,0))
    ship.marker("Maneuver_crescent",(-5.21,-.28,-2.39),(-1,0,0))
    ship.marker("Maneuver_keel",(x,-2.28,7.67),(0,-1,0))
    ship.marker("Maneuver_fore",(0,-.35,-10.22),(0,0,-1))
    forms.signal(-1,(-4.67,.21,-2.83))
    forms.signal(1,(4.21,1.22,4.10))
    ship.marker("EquipmentMount_crescent",(-4.25,.23,-3.09))
    ship.marker("EquipmentMount_salvage",(x,2.25,5.91))
    ship.marker("DockMount",(0,-1.53,7.37))
    return [("S C A V E N G E R",(-.78,2.81,-4.56),.28)]


SHAPES = {shape.__name__: shape for shape in (pipit, skua, cairn, dredge, ibis, kestrelwing, osprey, scavenger)}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("key", choices=SHAPES)
    key = parser.parse_args(sys.argv[sys.argv.index("--") + 1:]).key
    build_variant(key, SHAPES[key], __file__, dependencies=(forms.__file__,))
