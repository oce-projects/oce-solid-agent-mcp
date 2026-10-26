using System.Collections.Generic;

using static Oce.Solid.Agent.Core.Catalog.Node;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// THE CATALOG. What the model can ask for, and how it is written.
    ///
    /// IT LIVES HERE AND NOT IN A HAND-WRITTEN JSON, and not in the server either, for
    /// one reason only: **the layer that DESCRIBES an operation must be
    /// the same one that EXECUTES it**. It is the only arrangement in which
    /// an operation that is described and not executable cannot enter the
    /// catalog without somebody noticing: the check becomes
    /// an assertion inside a project, instead of a comparison between two
    /// repositories that nobody redoes.
    ///
    /// It used to live in the drawing module of the server, and whoever cloned this repository COULD NOT
    /// regenerate `catalog.json`: the server, docker and a
    /// disposable container were needed. Now `Oce.Solid.Agent.Catalog.exe` is needed.
    ///
    /// `Tested` does not end up in the JSON and this is intended: it is the promise that
    /// the operation was built and MEASURED inside SOLIDWORKS, and
    /// as long as it is false the operation is not exported — so the model does not
    /// see it. It sits next to the code that executes, not in a distant file.
    /// </summary>
    public static class Operations
    {
        /// <summary>The families, with the line that explains them.</summary>
        public static readonly List<KeyValuePair<string, string>> Families =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "extrusions",
                    "making the first body of the part"),
                new KeyValuePair<string, string>(
                    "features",
                    "removing or changing material on a body that already exists"),
                new KeyValuePair<string, string>(
                    "weldments",
                    "metalwork: bars, frames, cuts between bars, cut list"),
                new KeyValuePair<string, string>(
                    "references",
                    "planes and axes to build on"),
                // THE FAMILY THAT IS NOT A PART. All the others work
                // inside a part document; this one puts together parts that
                // already exist, and what distinguishes it is not what it does but what
                // it is NOT enough: inserting is not mating, and an assembly of
                // free components looks assembled without being so. The line
                // below tells the model before it asks for the list.
                new KeyValuePair<string, string>(
                    "assemblies",
                    "putting together parts that already exist. Inserting is " +
                    "NOT mating: always look at «resting»"),
                new KeyValuePair<string, string>(
                    "physical_properties",
                    "what the part IS, not its shape"),
                new KeyValuePair<string, string>(
                    "drawing",
                    "putting the part on a drawing and handing it over: sheets, views, dimensions, PDF"),
                // THE FAMILY THAT DOES NOT DRAW. Up to 0.76.0 no
                // operation of the catalog could answer «how did it turn out?»:
                // the measurements were there but they were private inside the operations, and the
                // model could not call any of them.
                new KeyValuePair<string, string>(
                    "checks",
                    "saying how it turned out, without touching it"),
                new KeyValuePair<string, string>(
                    "exports",
                    "getting the part out of SOLIDWORKS: STL for 3D printing, STEP "
                    + "and IGES for whoever has to machine it"),
                // WHAT THE PART SAYS ABOUT ITSELF. The drawing number and the
                // revision are not geometry, but they are what reaches the
                // title block and the PDM card: a right part with the wrong
                // revision is a wrong part.
                new KeyValuePair<string, string>(
                    "identity",
                    "what the part says about itself: drawing number, revision, "
                    + "customer, and its variants"),
                // SHEET METAL (entry GP18). The family that produces the file that goes
                // to the cutting machine: a silent defect here costs real
                // sheet, and the flat pattern is always compared with the hand calculation.
                new KeyValuePair<string, string>(
                    "sheet_metal",
                    "bent sheet metal and its flat pattern as DXF for cutting"),
                // THE DIMENSIONS IN THE MODEL (entry Z-H3): DimXpert from the core. It is not
                // the drawing: the dimensions are on the part.
                new KeyValuePair<string, string>(
                    "dimensions",
                    "dimensions and tolerances put on the PART (DimXpert), not on a drawing"),
            };

        public static readonly List<OperationSpec> All =
            new List<OperationSpec>
            {
                new OperationSpec
                {
                    Name = "extrude_circle",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"diameter_mm\":6,\"height_mm\":10}",   // from `thread_on_pin`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A solid cylinder centered on the origin.",
                    Description =
                        "Creates a solid cylinder: a circle on a plane, extruded. "
                        + "It is the operation for pins, dowels, bushes, discs."
                        + "\n\nOn a part that already has a body, the new cylinder merges with the "
                        + "bodies it touches (merge and automatic body selection are on, as the guide "
                        + "describes them); it cannot merge with a body it does not touch, and the "
                        + "document state counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "diameter_mm", O(
                                "type", "number"),
                            "height_mm", O(
                                "type", "number",
                                "description", "length of the cylinder, in mm")),
                        "required", A(
                            "diameter_mm",
                            "height_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_profile",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Any shape, given by contours, extruded: the general "
                        + "way to make the first body.",
                    Description =
                        "Extrudes ANY SHAPE, given as a list of vertices. It is the "
                        + "operation for everything that is not a rectangle or a circle: shaped "
                        + "brackets, angle brackets, gussets, angles, and HOLLOW profiles "
                        + "— tubes, pipes, C sections — which are made with TWO contours, "
                        + "an outer one and an inner one.\n\nThe first contour is the outer "
                        + "edge; the ones after it are voids inside it. Each contour "
                        + "closes by itself: the last vertex joins the first, do not repeat "
                        + "it.\n\nIf the shape you need has a name (a gear, a tubular "
                        + "section), see whether a dedicated operation exists: it is easier for "
                        + "whoever confirms to check, because it shows the dimensions "
                        + "instead of a list of points."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason."
                        + "\n\nOn a part that already has a body, the new shape merges with the bodies "
                        + "it touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    // AN OPEN PART, with or without bodies (brief R20): the
                    // description above says it merges with the bodies it
                    // touches, and on 24/10/2026 every extrusion after the first
                    // ran on a part with a body.
                    Precondition = "an open part (empty, or with bodies to add to)",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right. WHERE THE "
                                    + "CONTOUR'S AXES GO (brief R7): «sketch_axes» in the answer names, for "
                                    + "the plane used, the model axis each contour axis runs along and the "
                                    + "direction of the extrusion."),
                            "contours", O(
                                "type", "array",
                                "description", "The FIRST contour is the outer edge, the following ones are the voids "
                                    + "inside it.\n\nEach contour can have TWO forms:\n1) a "
                                    + "closed polyline: a list of vertices, for example "
                                    + "[[0,0],[100,0],[100,60],[0,60]]. The last vertex joins the "
                                    + "first by itself: do NOT repeat it.\n2) a CIRCLE, and for a hole or a "
                                    + "round bar you must use this and not a polyline with many sides: "
                                    + "{\"circle\": [x_center, y_center], \"diameter_mm\": 20}. The two numbers in brackets are the center. A circle "
                                    + "approximated by segments has material inside the circumference, "
                                    + "and a shaft does not fit in it.\n\nExample, PIPE outer diameter 60 "
                                    + "wall 5:\n[{\"circle\": [0, 0], \"diameter_mm\": 60}, {\"circle\": "
                                    + "[0, 0], \"diameter_mm\": 50}]\nExample, SQUARE TUBE 80x80 wall "
                                    + "5:\n[[[-40,-40],[40,-40],[40,40],[-40,40]], "
                                    + "[[-35,-35],[35,-35],[35,35],[-35,35]]]",
                                "items", O(
                                    "description", "a polyline (list of [x_mm, y_mm]) or a circle "
                                        + "({\"circle\": [x, y], \"diameter_mm\": d})")),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "how far it is extruded, in mm"),
                            "symmetric", O(
                                "type", "boolean",
                                "description", "extrusion half on one side and half on the other of the plane (default: no)")),
                        "required", A(
                            "contours",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_rectangle",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"length_mm\":200,\"width_mm\":120,\"thickness_mm\":20,\"centered\":true}",   // from `blind_slot`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A solid rectangular plate, the first body of the part.",
                    Description =
                        "Creates a rectangular plate: draws a rectangle on a plane and "
                        + "extrudes it. It is the operation for plates, brackets, bases, square "
                        + "flanges."
                        + "\n\n«plane» takes «front», «top» or «right» (the case of "
                        + "the letters does not matter); any other name is refused, it never "
                        + "falls back to the front plane."
                        + "\n\nOn a part that already has a body, the new plate merges with the bodies "
                        + "it touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "length_mm", O(
                                "type", "number",
                                "description", "first side, in mm"),
                            "width_mm", O(
                                "type", "number",
                                "description", "second side, in mm"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "HOW FAR IT IS EXTRUDED, in mm. For a plate it is the thickness; "
                                    + "for a bar or a profile it is the LENGTH. The rectangle is the section, "
                                    + "this is how far it goes in depth: an 80x80 bar 1000 long is made "
                                    + "with length_mm 80, width_mm 80, thickness_mm 1000."),
                            "centered", O(
                                "type", "boolean",
                                "description", "rectangle centered on the origin (default: yes)")),
                        "required", A(
                            "length_mm",
                            "width_mm",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_section",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A bar from a standard section (tubular, round, angle, U, T, "
                        + "flat, hexagon, slot) that is NOT metalwork.",
                    Description =
                        "Extrudes a standard section — tubular, round, tube, "
                        + "L angle, U or T section, flat, hexagon, slot — and "
                        + "gets a SOLID BODY with that shape.\n\nIT IS NOT THE "
                        + "METALWORK OPERATION. For a frame element use "
                        + "«structural_member»: an extruded body has the same shape as a "
                        + "bar, but it is not a structural member, so it does NOT generate the "
                        + "cut list, does not carry the name of the profile, and cannot be "
                        + "mitered at 45 degrees at the joints. Whoever goes to the workshop with "
                        + "a part like this does not have the list of bars to cut.\n\nThis "
                        + "operation is for when the section is NOT a frame element: "
                        + "a spacer cut from bar stock, a small shaped plate, a "
                        + "part turned from round bar, or a profile that "
                        + "is not in the company library.\n\nThe dimensions to give change with the type:\n• tubular  "
                        + "width, height, wall\n• tube      diameter, wall\n• round   "
                        + "  diameter\n• angle / u / t   width, height, thickness\n• "
                        + "flat / slot     width, height\n• hexagon   across flats "
                        + "(the width across the flats, not the "
                        + "circumscribed diameter)\n\n«length_mm» is how long the bar is: for an "
                        + "80x80x5 tubular 1000 long that is width 80, height 80, wall "
                        + "5, length 1000.\n\nTHE EDGES ARE SHARP, and it shows in the "
                        + "weight. A commercial profile has rounded corners, so "
                        + "this section weighs more: an 80x80x5 tubular 1000 long "
                        + "comes out at 1,500,000 mm3 against about 1,443,000 for the real profile, "
                        + "that is 4% more. For the shape nothing changes; if the "
                        + "number you need is the WEIGHT, take the profile from the "
                        + "library with «structural_member».\n\nTHE SLOT IS A POLYGON: its "
                        + "two ends are 24 sides each, with the vertices on the "
                        + "circumscribed circle, so that the slot is never narrower than the "
                        + "nominal one. It comes out 0.2% wider (20.043 for 20) and with 0.2% "
                        + "more area. Round and tube are true circles."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason."
                        + "\n\nOn a part that already has a body, the new bar merges with the bodies it "
                        + "touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "variant", O(
                                "type", "string",
                                "enum", A(
                                    "tubular",
                                    "round",
                                    "tube",
                                    "angle",
                                    "u",
                                    "t",
                                    "flat",
                                    "hexagon",
                                    "slot"),
                                "description", "which section"),
                            "length_mm", O(
                                "type", "number",
                                "description", "how long the bar is, in mm"),
                            "width_mm", O(
                                "type", "number"),
                            "height_mm", O(
                                "type", "number"),
                            "wall_mm", O(
                                "type", "number",
                                "description", "wall thickness, for tubular and tube"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "thickness of the profile, for angle, U and T"),
                            "diameter_mm", O(
                                "type", "number"),
                            "across_flats_mm", O(
                                "type", "number",
                                "description", "width across flats of the hexagon"),
                            "symmetric", O(
                                "type", "boolean",
                                "description", "half on each side of the plane (default: no)")),
                        "required", A(
                            "variant",
                            "length_mm")),
                },
                new OperationSpec
                {
                    Name = "revolve",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"angle_deg\":360,\"axis\":[[0,0],[90,0]],\"contours\":[[[0,0],[0,15],[40,15],[40,10],[70,10],[70,17],[90,17],[90,0]]]}",   // from `drilled_shaft_no_longer_declares_profile`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A solid of revolution: bushes, spacers, stepped shafts, "
                        + "flanges, pulleys, handwheels.",
                    Description =
                        "Revolves a profile about an axis and gets a body of revolution. It "
                        + "is the operation for everything made on the lathe when the shape "
                        + "VARIES along the axis: stepped shafts, bushes with grooves, flanges, "
                        + "pulleys. For a constant cylinder «extrude_circle» or "
                        + "«extrude_section» are enough, and simpler.\n\nThe profile is given with "
                        + "the same CONTOURS as «extrude_profile»: the first is the edge, "
                        + "the following ones are voids.\n\nTHE DIFFERENCE IS THE AXIS. The "
                        + "profile is a HALF SECTION, seen from the side as on the lathe: it lies "
                        + "all on one side of the axis, touching it at most. It must "
                        + "NOT cross it — a profile that crosses the axis produces a "
                        + "solid that intersects itself.\n\nExample, stepped shaft: diameter 30 "
                        + "for 40 mm, then diameter 20 for another 30, axis along y=0:\n  "
                        + "contours: [[[0,0],[0,15],[40,15],[40,10],[70,10],[70,0]]]\n  axis: "
                        + "[[0,0],[70,0]]\nThe side from (70,0) to (0,0), which closes the polyline, "
                        + "lies ON the axis: this is the normal case, not an error.\n\n"
                        + "Solid body only: for a thin wall draw the inner void as a second "
                        + "contour."
                        + "\n\nI REFUSE IF THE AXIS IS NOT ON THE PLANE OF THE PROFILE, and if the profile "
                        + "CROSSES the axis. A profile straddling the axis generates a solid that "
                        + "interpenetrates itself: SOLIDWORKS sometimes makes it all the same, and the "
                        + "part that comes out is not the one you wanted."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "contours", O(
                                "type", "array",
                                "description", "the profile to revolve, same syntax as «extrude_profile»: the first "
                                    + "contour is the edge, the following ones are voids. Each "
                                    + "contour is a closed polyline of vertices [x_mm, y_mm] (the last one "
                                    + "closes by itself, do not repeat it) or {\"circle\": [x, y], "
                                    + "\"diameter_mm\": d}. It lies all on one side of the axis.",
                                "items", O(
                                    "description", "a polyline (list of [x_mm, y_mm]) or a circle "
                                        + "({\"circle\": [x, y], \"diameter_mm\": d})")),
                            "axis", O(
                                "type", "array",
                                "description", "the line of revolution: TWO points [x_mm, y_mm] in the same system "
                                    + "as the contours. For a turned part it is almost always the horizontal axis "
                                    + "of the drawing, for example [[0,0],[70,0]]. It is not a detail: it "
                                    + "decides the final bounding box as much as the vertices of the profile do.",
                                "items", O(
                                    "type", "array",
                                    "items", O(
                                        "type", "number"))),
                            "angle_deg", O(
                                "type", "number",
                                "description", "default 360, one full turn")),
                        "required", A(
                            "contours",
                            "axis")),
                },
                new OperationSpec
                {
                    Name = "spur_gear",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "An involute gear, with bore and keyway.",
                    Description =
                        "Creates a cylindrical SPUR GEAR, from the number of teeth and "
                        + "the module. The tooth profile is an involute and I calculate "
                        + "it: you give only the design data.\n\nWith bore_mm you also "
                        + "get the central bore.\n\nI REFUSE IF there are fewer than 17 "
                        + "teeth (with the standard pressure angle of 20 degrees), because a "
                        + "profile correction against root undercut would be needed and I cannot "
                        + "do it. It is a limit of mine, not of mechanics: with a "
                        + "larger pressure angle the minimum goes down (25 degrees: 11 teeth)."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "teeth", O(
                                "type", "integer",
                                "description", "number of teeth (z)"),
                            "module_mm", O(
                                "type", "number",
                                "description", "the module m in mm. Pitch diameter = m x z, outside diameter = m x (z+2)."),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "face width"),
                            "pressure_angle_deg", O(
                                "type", "number",
                                "description", "default 20, which is the standard"),
                            "bore_mm", O(
                                "type", "number",
                                "description", "diameter of the central bore; leave it out for a solid gear"),
                            "keyway", O(
                                "type", "boolean",
                                "description", "keyway in the bore, with standard width and depth (ISO/R "
                                    + "773, DIN 6885-1) taken from the diameter of the bore. Do not ask "
                                    + "for the dimensions of the keyway: the standard chooses them.")),
                        "required", A(
                            "teeth",
                            "module_mm",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "slot",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "An elongated hole: the fastening that can be adjusted at assembly.",
                    Description =
                        "Cuts a SLOT in a face: an elongated hole with round ends. "
                        + "It is what lets you ADJUST on site — a bracket with two slots can be "
                        + "lined up, one with two round holes either fits or has to be made "
                        + "again.\n\nTHE LENGTH IS OVERALL, END TO END, as it is measured on "
                        + "the part and as it is written on the drawing; the WIDTH is the diameter "
                        + "of the ends, that is, the diameter of the screw plus the clearance. "
                        + "For an M10 that must slide 40 mm: width 11, length 51.\n\n"
                        + "«angle_deg» rotates the slot in the plane of the face: 0 is "
                        + "horizontal, 90 vertical. It is needed when the adjustment "
                        + "must go in a specific direction.\n\nIt is not a rectangular "
                        + "pocket. «rectangular_cut» looks like it but has sharp corners, and "
                        + "the screw rattles inside it instead of sliding."
                        + "\n\nI REFUSE IF ONE OF FOUR CASES APPLIES, and I list them up front because finding out by "
                        + "trying costs a round: the slot is not through and you do not give me the "
                        + "depth; it falls entirely outside the material (I tell you between which "
                        + "coordinates the face lies); it is larger than the face and would cover "
                        + "it all; or length and width are equal -- that is a circle, and here the "
                        + "length is measured end to end.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "a face that already exists, taken from the state"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan. Give "
                                    + "this OR face_uid, not both.\n\nAllowed kinds:\n- pocket_floor: "
                                    + "the floor of the pocket (it exists only if the pocket is "
                                    + "blind)\n- bottom_face: the face on the sketch plane, "
                                    + "the bottom of the extrusion\n- top_face: the face at the "
                                    + "top of the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "MODEL COORDINATES in mm, not face coordinates. Take the two axes "
                                    + "that remain after removing the one along the face's normal, in "
                                    + "the order X, Y, Z: normal along Z -> x_mm is the model's X and "
                                    + "y_mm the Y; normal along Y -> x_mm is the X and y_mm the Z; normal "
                                    + "along X -> x_mm is the Y and y_mm the Z. They are the same "
                                    + "numbers as the «point_mm» you read in the document state, so you can "
                                    + "start from those. The third coordinate is not needed: it is the "
                                    + "plane of the face."),
                            "y_mm", O(
                                "type", "number",
                                "description", "see x_mm"),
                            "anchor", AnchorSchema("the slot"),
                            "length_mm", O(
                                "type", "number",
                                "description", "TOTAL length, end to end, not the travel. It must be "
                                    + "greater than the width."),
                            "width_mm", O(
                                "type", "number",
                                "description", "width of the slot, that is, the diameter of the round ends"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "rotation in the plane of the face, default 0. The sketch is born "
                                    + "dimensioned (radius and distance between centres): at 0 and 90 degrees "
                                    + "the orientation is fixed too; when oblique, the angle is dimensioned "
                                    + "against the first edge of the «anchor», and with no anchor it stays free "
                                    + "(I tell you). The angle dimension is read back: it must measure the angle "
                                    + "between the slot and that edge (or its supplement), or I refuse. "
                                    + "If the four tangencies and the equal radii of the slot cannot be "
                                    + "counted, or not all of them were born, the response says so in "
                                    + "«slot_relations»."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "default: yes"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "only if not through")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_on_face",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "GROWS material from a face: a boss, a lip, "
                        + "a spacer, a tab.",
                    Description =
                        "Draws an outline on a face that already exists and extrudes it "
                        + "OUTWARD, adding material to the part.\n\nIT IS THE OPERATION "
                        + "THAT GROWS A BOSS. The others on an existing part remove (holes, "
                        + "cuts, slots), hollow out (shell) or move; «engrave_text» in relief "
                        + "also adds material, but only lettering, and so does "
                        + "«combine_bodies», which joins bodies that already exist. Without this "
                        + "one you cannot make: the sealing lip of a cover, the spacers "
                        + "something rests on, a fixing tab, a post for a screw, a boss "
                        + "around a hole.\n\nTHE OUTLINES ARE IN THE COORDINATES OF THE FACE, as "
                        + "for «rectangular_cut»: (0, 0) is the centre of the face. "
                        + "Several nested outlines make a frame — the outer one and "
                        + "the inner one — and that is how you make a lip around an edge "
                        + "instead of a solid block.\n\n«merge» true (the default) fuses the "
                        + "boss with the part. With false a SEPARATE BODY is born: almost never "
                        + "what you want, and the tree grows the same, so you notice only "
                        + "by counting the bodies."
                        + "\n\nI REFUSE IF the boss adds no material (it grew INTO the part), if, with «merge», the "
                        + "bodies are not the same as before, or if you ask for the «anchor» "
                        + "on more than one outline or on an outline that is neither round nor an "
                        + "axis-aligned rectangle: the point the anchor holds is the centre, "
                        + "and I cannot guess the centre of an arbitrary polygon.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "the face to grow the material from"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan. Give "
                                    + "this OR face_uid, not both.\n\nAllowed kinds:\n- pocket_floor: "
                                    + "the floor of the pocket (it exists only if the pocket is "
                                    + "blind)\n- bottom_face: the face on the sketch plane, "
                                    + "the bottom of the extrusion\n- top_face: the face at the "
                                    + "top of the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "contours", O(
                                "type", "array",
                                "description", "the shape to extrude, in the coordinates of the face. Each "
                                    + "outline is {\"vertices\": [[x, y], ...]} — a closed polyline, "
                                    + "the last point closes itself — or {\"circle\": [x, y], "
                                    + "\"diameter_mm\": d}. Two nested outlines make a frame.",
                                "items", O(
                                    "type", "object")),
                            "height_mm", O(
                                "type", "number",
                                "description", "how far it sticks out from the face"),
                            "anchor", AnchorSchema("the boss", true),
                            "merge", O(
                                "type", "boolean",
                                "description", "default: yes. With false it makes a separate body.")),
                        "required", A(
                            "contours",
                            "height_mm")),
                },
                new OperationSpec
                {
                    Name = "hole_wizard",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A hole the way a hole is made: guided creation (Hole Wizard), ISO "
                        + "standard, threaded or clearance, counterbored or countersunk, even several "
                        + "holes at once.",
                    Description =
                        "THE RIGHT WAY TO MAKE A HOLE: the SolidWorks Hole Wizard, ISO "
                        + "standard. To be preferred ALWAYS over «cylindrical_cut» when "
                        + "the hole houses a screw, a pin or a thread.\n\nIt takes the sizes from "
                        + "the standard: you give the size of the screw («M8») and the type, "
                        + "not the diameters. A threaded M8 hole comes out with the "
                        + "6.8 tap drill and the thread; a clearance M8 comes out 9.\n\nSeveral "
                        + "identical holes are made in ONE SINGLE call by giving several "
                        + "«positions»: that is how a series of holes is drawn, and in the shop "
                        + "it reads as a single operation."
                        + "\n\nEach position can carry an «anchor» (a distance from an edge, or "
                        + "the centre between two edges, per axis): then the hole follows the "
                        + "edge when the part changes. I REFUSE IF only some positions carry an "
                        + "anchor (all or none: the holes are in one sketch), if a distance is "
                        + "less than the radius of the hole, or if, on a centred axis, the gap "
                        + "between the two edges is narrower than the hole. The radius is the "
                        + "widest cut: the counterbore for «counterbore», the countersink for "
                        + "«countersink». I know it only once the hole is made, and then I remove "
                        + "the hole. If SOLIDWORKS does not declare the diameter, the response "
                        + "says so in «anchor_fit_not_checked».\n\nI REFUSE IF the hole rebuilds in error "
                        + "or removes no material (for example a position that lies "
                        + "outside the face), and I remove the hole.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "a face that already exists, taken from the state"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan. Give "
                                    + "this OR face_uid, not both.\n\nAllowed kinds:\n- pocket_floor: "
                                    + "the floor of the pocket (it exists only if the pocket is "
                                    + "blind)\n- bottom_face: the face on the sketch plane, "
                                    + "the bottom of the extrusion\n- top_face: the face at the "
                                    + "top of the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "variant", O(
                                "type", "string",
                                "enum", A(
                                    "clearance",
                                    "threaded",
                                    "counterbore",
                                    "countersink"),
                                "description", "clearance = plain hole for the screw to pass through; threaded "
                                    + "= tapped, the screw threads into it; counterbore = with the seat for "
                                    + "the cylindrical head (ISO 4762); countersink = with the conical seat for "
                                    + "the countersunk head (ISO 10642). Default: clearance."),
                            "size", O(
                                "type", "string",
                                "enum", A(
                                    "M3",
                                    "M4",
                                    "M5",
                                    "M6",
                                    "M8",
                                    "M10",
                                    "M12",
                                    "M14",
                                    "M16",
                                    "M20",
                                    "M24"),
                                "description", "the size of the SCREW, not the diameter of the hole. Copy it "
                                    + "from this list: it is what the SolidWorks ISO table knows."),
                            "positions", O(
                                "type", "array",
                                "description", "where the holes go, on the plane of the face. Several identical "
                                    + "holes go in a single call. The «anchor» is given to ALL the positions "
                                    + "or to none: the holes are in one sketch, and a point without an anchor "
                                    + "would be left free to move. I REFUSE IF a distance is less than "
                                    + "the radius of the hole, or a centred gap is narrower than the hole, "
                                    + "counterbore or countersink included (the ISO table decides it): the "
                                    + "hole is already made, and I remove it.",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "x_mm", O(
                                            "type", "number",
                                            "description", "MODEL COORDINATES in mm, not face coordinates. Take the two axes "
                                                + "that remain after removing the one along the face's normal, in "
                                                + "the order X, Y, Z: normal along Z -> x_mm is the model's X and "
                                                + "y_mm the Y; normal along Y -> x_mm is the X and y_mm the Z; normal "
                                                + "along X -> x_mm is the Y and y_mm the Z. They are the same "
                                                + "numbers as the «point_mm» you read in the document state, so you can "
                                                + "start from those. The third coordinate is not needed: it is the "
                                                + "plane of the face."),
                                        "y_mm", O(
                                            "type", "number",
                                            "description", "see x_mm"),
                                        "anchor", AnchorSchema("the hole")),
                                    "required", A(
                                        "x_mm",
                                        "y_mm"))),
                            "through_all", O(
                                "type", "boolean",
                                "description", "default: yes"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "required if not through")),
                        "required", A(
                            "size",
                            "positions")),
                },
                new OperationSpec
                {
                    Name = "shell",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Hollows out a solid body leaving a wall of constant thickness: "
                        + "boxes, housings, covers.",
                    Description =
                        "The shell: takes a solid body and hollows it out, leaving a wall "
                        + "of constant thickness. The faces named in «faces_uid» are REMOVED, "
                        + "that is, they stay open — a single face opens a box on one "
                        + "side, two opposite faces make a tube open at both ends.\n\nThe thickness is "
                        + "taken off toward the INSIDE: the outer envelope does not "
                        + "change, as when opening a box. With «outward» the thickness is added "
                        + "on the outside, and it is for when the INNER envelope is the "
                        + "fixed dimension — a compartment that must stay the given size.\n\nA "
                        + "shell with no face removed is a closed hollow body: legitimate "
                        + "but rare, and almost always the sign that a face is missing from "
                        + "the request. That is why it must be confirmed with «fully_closed».",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faces_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the faces to REMOVE (they stay open), taken from the document state"),
                            "faces_ref", O(
                                "type", "array",
                                "description", "the faces to REMOVE, when they are created by an EARLIER step "
                                    + "of this same plan and so have no identifier yet. Give this OR "
                                    + "«faces_uid», not both.\n\nOne element for each face to open: "
                                    + "«make a block and then hollow it from the top» is a single "
                                    + "plan.\n\nAllowed kinds:\n- top_face: the face at "
                                    + "the top of the extrusion, the one opposite the sketch plane"
                                    + "\n- bottom_face: the face on the sketch plane\n- "
                                    + "pocket_floor: the floor of a blind pocket\n"
                                    + "- hole_face: the cylindrical wall of a hole",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "step", O(
                                            "type", "integer",
                                            "description", "the sequence number of the step that creates the "
                                                + "geometry, counting from 1. It must be a step BEFORE this one."),
                                        "kind", O(
                                            "type", "string",
                                            "enum", A(
                                                "top_face", "bottom_face",
                                                "pocket_floor", "hole_face"))),
                                    "required", A("step", "kind"))),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "thickness of the wall that remains"),
                            "outward", O(
                                "type", "boolean",
                                "description", "default no: the thickness is taken off toward the inside and "
                                    + "the outer envelope does not change"),
                            "fully_closed", O(
                                "type", "boolean",
                                "description", "explicit confirmation for a shell with no face removed. Usually "
                                    + "a face is missing: use it only if this is really what you "
                                    + "need.")),
                        "required", A(
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "edit_extrusion",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"feature_uid\":\"Cut-Extrude1\",\"through_all\":true}",   // from `blind_becomes_through`
                    Summary = "Changes an extrusion or a cut already made: blind -> through all, or another depth.",
                    Description =
                        "Changes the definition of an extrusion or an extruded cut that already exists: a "
                        + "blind hole that must become through («through_all»: true), or a different depth "
                        + "(«depth_mm»). The part is not redone: the feature changes in its place in the tree, "
                        + "and the ones downstream follow it.\n\nAfterwards I read the definition back (condition "
                        + "and depth), the volume before and after, and the features in error: if the change "
                        + "breaks one downstream, I put it back as it was and I refuse. If the volume does not "
                        + "change I tell you (a blind cut deeper than the material is already through).\n\nI REFUSE "
                        + "IF the feature is not an extrusion or an extruded cut (for any other dimension there "
                        + "is «set_dimension»), if you ask for «through_all» on an extrusion — also on a boss made "
                        + "on a face with «extrude_on_face», which in the state has type «ICE» like the cuts "
                        + "but is an extrusion —, if the extrusion goes in TWO independent directions (I "
                        + "change and read back only the first, and the second would stay as it was), or if you do "
                        + "not give me exactly one of «through_all» and «depth_mm».",
                    Precondition = "a part with an extrusion or an extruded cut",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the feature to change, with the name from the state («feature»). The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "true: the cut goes through everything. Or «depth_mm»."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "the new depth, in mm (blind). Or «through_all».")),
                        "required", A("feature_uid")),
                },
                new OperationSpec
                {
                    Name = "reorder_feature",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"feature_uid\":\"Shell1\",\"relative_to_uid\":\"Cut-Extrude1\",\"placement\":\"before\"}",   // from `order_changes_the_part`
                    Summary = "Moves a feature before or after another in the tree, and says how the part changed.",
                    Description =
                        "Moves a feature in the tree, before or after another: a shell placed before a "
                        + "hole gives a different part than one placed after (measured: 56485.7 against "
                        + "51952.38 mm3 on the same box). I give you the order before and after, the "
                        + "volume and the bodies before and after; if the part does not change I tell you."
                        + "\n\nIF THE MOVE BREAKS A FEATURE DOWNSTREAM I put it back where it was and "
                        + "I refuse.\n\nI REFUSE IF one of the two features is not there or is no longer "
                        + "the one in the state (a name can change, the ID cannot), if «placement» is not "
                        + "«before» or «after», or if SOLIDWORKS does not move it: that happens when it "
                        + "depends on a feature that would come after (a hole before the extrusion it sits "
                        + "on).",
                    Precondition = "a part with at least two features",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the feature to move, with the name from the state («feature»). The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "relative_to_uid", O(
                                "type", "string",
                                "description", "the feature to put it next to."),
                            "placement", O(
                                "type", "string",
                                "enum", A("before", "after"),
                                "description", "before or after «relative_to_uid».")),
                        "required", A("feature_uid", "relative_to_uid", "placement")),
                },
                new OperationSpec
                {
                    Name = "global_variable",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"name\":\"wall\",\"value_mm\":12}",   // from `wall_has_a_name`
                    Summary = "Gives a NAME to a number (wall = 3 mm): the linked dimensions change with it.",
                    Description =
                        "Creates or changes a global variable of the part: «wall» = 12 mm, or an expression "
                        + "(«2 * \"wall\" + 1mm»). The dimensions linked with «link_dimension» "
                        + "change when you change it: it is the way to make a part that can be modified "
                        + "without breaking, without repeating the same number in ten places.\n\n"
                        + "After writing it I read it back, and I tell you which dimensions it drives and "
                        + "the volume before and after.\n\nI REFUSE IF the name is not made of letters, "
                        + "digits and «_», if you give me both (or neither) of «value_mm» and "
                        + "«expression», or if SOLIDWORKS does not accept the name: it reserves some in its own language, such as "
                        + "the word for the thickness (the refusal says which) — use for example «wall».",
                    Precondition = "an open part",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "the name of the variable: letters, digits and «_», starting with a letter."),
                            "value_mm", O(
                                "type", "number",
                                "description", "the value, in millimetres. Or «expression»."),
                            "expression", O(
                                "type", "string",
                                "description", "a SOLIDWORKS expression, with the names between double "
                                    + "quotes: «2 * \"wall\" + 1mm». Or «value_mm».")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "link_dimension",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary = "Links a dimension to a variable or to a calculation: the equation drives it.",
                    Description =
                        "Links a dimension of the part (its name from the state, «D1@Sketch1») to an "
                        + "expression: a global variable («\"wall\"») or a calculation («2 * \"wall\" "
                        + "+ 1mm»). From then on the equation drives the dimension, and it changes "
                        + "when the variable changes; «set_dimension» no longer changes it."
                        + "\n\nAfter the link I read the dimension back: it must equal what the equation says, "
                        + "and I give you the volume before and after.\n\nI REFUSE IF the dimension is not "
                        + "there (I tell you which ones there are) or if SOLIDWORKS does not "
                        + "accept the expression (a variable that does not exist: create it first "
                        + "with «global_variable»).",
                    Precondition = "an open part with at least one dimension",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "the name of the dimension, copied from the document state. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "expression", O(
                                "type", "string",
                                "description", "what to link it to, with the names between double quotes: «\"wall\"», "
                                    + "«2 * \"wall\" + 1mm».")),
                        "required", A("dimension", "expression")),
                },
                new OperationSpec
                {
                    Name = "set_dimension",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Changes a dimension of a feature that already exists, without redoing anything.",
                    Description =
                        "Changes the VALUE of a dimension — a thickness, a depth, a diameter, "
                        + "a radius — on a feature the part already has.\n\nIT IS THE "
                        + "MOST FREQUENT REQUEST IN THE SHOP: «take that hole to M10», "
                        + "«make the thickness 3». Before, the only way was to delete and redo, "
                        + "that is, to lose everything that came after — holes, fillets, ribs "
                        + "built on that geometry.\n\nYOU FIND THE NAME OF THE DIMENSION IN "
                        + "THE DOCUMENT STATE: every feature carries its own, with name and value "
                        + "in mm (for example «D1@Shell1@Part3.Part» = 6). "
                        + "Copy the name as it is.\n\nThe value changes in ALL the configurations: "
                        + "changing just one would leave the part different depending "
                        + "on how you look at it.\n\nIf the dimension changes but the volume "
                        + "of the part does not, it is said: almost always it means that "
                        + "this dimension does not drive the geometry you had in mind.\n\nI "
                        + "REFUSE IF the new dimension makes an opening break out of the material "
                        + "— a hole or a slot that was inside the part and would open its "
                        + "edge, or two openings that would merge into one: I put the previous "
                        + "value back, and say so. It happens more often than it seems, because "
                        + "a plate drawn centred grows and shrinks FROM ONE SIDE ONLY: shortening "
                        + "it by 50 moves one side by 50, not two sides by 25."
                        + "\n\nI REFUSE IF with the new dimension a chamfer or a fillet no longer fits — for example "
                        + "a thickness below the side of the chamfer: SOLIDWORKS gives no "
                        + "error, it changes the shape by itself, and the base feature loses a whole "
                        + "face. I put the previous value back and say which chamfer or fillet."
                        + "\n\nAN ANGULAR DIMENSION (the state writes it in «deg») is changed in "
                        + "DEGREES: «value_mm» carries the degrees, and the answer says «before_deg» "
                        + "and «after_deg» instead of «before_mm» and «now_mm». A dimension "
                        + "the document state lists with its value null (not 0) could "
                        + "not be read from SOLIDWORKS; set_dimension refuses it before changing anything.\n\nI "
                        + "REFUSE "
                        + "IF the dimension is read-only (I do not override those, even if SOLIDWORKS "
                        + "would allow it), driven (they measure, they do not command), or an "
                        + "integer one, like the number of copies of a pattern.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "the name of the dimension, copied from the document state. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "value_mm", O(
                                "type", "number",
                                "description", "the new value, in millimetres; in degrees if the dimension "
                                    + "is angular")),
                        "required", A(
                            "dimension",
                            "value_mm")),
                },
                new OperationSpec
                {
                    Name = "dimension_tolerance",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "The tolerance on a model dimension (±, bilateral, ISO fit), verified and with the right decimals.",
                    Description =
                        "Puts the TOLERANCE on a model dimension: symmetric (±value), bilateral (upper and "
                        + "lower deviations) or an ISO fit (class, e.g. H7 for a hole, g6 for a shaft). From the "
                        + "model dimension it reaches the drawing by itself with «model_dimensions».\n\nTHE "
                        + "FIT IS VERIFIED: the deviations are computed by SOLIDWORKS, and I check that the "
                        + "width of the tolerance band is the IT grade of the ISO 286-1 table for that diameter "
                        + "(IT5..IT11, up to 500 mm) and that the sign is the one of the letter (H from zero upward, "
                        + "h from zero downward). If it does not match I put it back as it was and tell you."
                        + "\n\nTHE DECIMALS: I set those of the tolerance on the dimension. Measured: without that, "
                        + "an H7 on 10 came out on the drawing as «10 H7 +0 / 0» — in the shop that means "
                        + "«exact». The tolerance decimals are set and read back on EVERY displayed copy of the "
                        + "dimension; one that does not take them is a refusal, and the tolerance is put "
                        + "back and read back. precision_status reports a SetPrecision3 answer of 1, 2 "
                        + "or 4 (about values I leave unchanged).\n\n"
                        + "The answer carries deviations and limits (10.000 / 10.015 for a Ø10 H7).\n\nI REFUSE IF "
                        + "the IT grade is outside 5..11 or the dimension is over 500 mm (I cannot verify them), "
                        + "if a bilateral has the lower above the upper, and when the verification does not match.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "the name of the dimension, copied from the document state (e.g. «D1@Sketch2»). The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "variant", O(
                                "type", "string",
                                "enum", A("symmetric", "bilateral", "fit", "none"),
                                "description", "which tolerance. «none» removes it."),
                            "value_mm", O("type", "number", "description", "for «symmetric»: the ± (0.05 = ±0.05)."),
                            "upper_mm", O("type", "number", "description", "for «bilateral»: the upper deviation (e.g. 0.1)."),
                            "lower_mm", O("type", "number", "description", "for «bilateral»: the lower deviation (e.g. -0.05)."),
                            "fit_class", O("type", "string", "description", "for «fit»: the ISO class, uppercase for a hole (H7), lowercase for a shaft (g6).")),
                        "required", A("dimension", "variant")),
                },
                new OperationSpec
                {
                    Name = "suppress_feature",
                    Family = "features",
                    Example = "{\"feature_uid\":\"Cut-Extrude2\"}",   // from `limit_suppress_twice`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "SUPPRESSES (turns off) a feature of the tree, without deleting it. It can be turned back on.",
                    Description =
                        "Suppresses a feature: the part goes back to how it was without it, but "
                        + "the feature stays in the tree and can be turned back on with "
                        + "«reactivate».\n\nIt is the way to go when you want to SEE the part "
                        + "without a feature, without committing to it. To remove it "
                        + "for real there is «delete_feature», which cannot be "
                        + "undone.\n\nTHE NAME OF THE FEATURE IS IN THE DOCUMENT STATE, inside "
                        + "«feature» (for example «Cut-Extrude2»). Copy it as it is.\n\nTHE "
                        + "CHILDREN ARE SUPPRESSED WITH IT, and that is almost always what you want: "
                        + "suppressing a hole also suppresses its fillets. The answer says "
                        + "which ones were.\n\nAFTERWARDS THE IDENTIFIERS ARE NO LONGER VALID. "
                        + "Suppressing makes faces disappear, and the identifiers (F1, E4) are "
                        + "renumbered by area at every reading: read the part again before the next step."
                        + "\n\nI REFUSE IF THE FEATURE IS ALREADY SUPPRESSED, and I tell you to "
                        + "use the operation that reactivates it. Suppressing twice does nothing and "
                        + "gives no error: it is the easiest way to believe you changed the part "
                        + "without having touched it.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the name of the feature, copied from the document state. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "reactivate", O(
                                "type", "boolean",
                                "description", "true TURNS BACK ON a suppressed feature, instead of suppressing "
                                    + "it. Default: false.")),
                        "required", A(
                            "feature_uid")),
                },
                new OperationSpec
                {
                    Name = "delete_feature",
                    Family = "features",
                    Example = "{\"feature_uid\":\"Cut-Extrude1\"}",   // from `delete_first_hole`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "DELETES a feature from the tree. It cannot be undone with another operation.",
                    Description =
                        "Removes a feature from the tree for good. The sketch it had absorbed "
                        + "goes away with it, so nothing orphaned is left in the tree.\n\nIT "
                        + "CANNOT BE UNDONE with another operation: the only way back is the "
                        + "«undo» tool, and only if you have done nothing else "
                        + "afterwards. If you only want to see the part without that feature, use "
                        + "«suppress_feature», which can be turned back on.\n\nI REFUSE "
                        + "IF ANYTHING DEPENDS ON IT, and I tell you what. Deleting "
                        + "a feature that others depend on would make them all disappear, and "
                        + "SOLIDWORKS does it without asking: if it is really that one "
                        + "you want to remove, first remove what sits on top of it.\n\nTHE "
                        + "NAME OF THE FEATURE IS IN THE DOCUMENT STATE, inside «feature». "
                        + "Copy it as it is.\n\nAFTERWARDS THE IDENTIFIERS ARE NO LONGER "
                        + "VALID: read the part again before the next step.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the name of the feature, copied from the document state. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is.")),
                        "required", A(
                            "feature_uid")),
                },
                new OperationSpec
                {
                    Name = "fillet_edges",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Constant-radius fillet on one or more edges.",
                    Description =
                        "Rounds one or more edges with the same radius. The edges are "
                        + "indicated with the identifiers taken from the document state."
                        + "\n\nIf the edges were renumbered between the reading of the state and now (a "
                        + "hole made in between renumbers them), I find them again by signature: "
                        + "same length at the same place, and I say so in the answer. I REFUSE "
                        + "IF the edge you had seen is no longer as it was — shortened, split — "
                        + "instead of taking the nearest, or if the radius does not fit in half of "
                        + "the edge."
                        + "\n\nThe fillet follows tangent edges: one selected edge can round a whole "
                        + "loop (a rounded block: 1 edge selected, 8 faces made). The document state "
                        + "says so for every fillet: «selected_edges» is how many edges were selected, "
                        + "«faces» how many faces the fillet made. The answer carries «selected_edges» "
                        + "and «faces» too, read the same way."
                        + "\n\nI REFUSE IF the volume of the part does not change after the fillet is "
                        + "made: the feature is in the tree and worked nothing, and I say it is still "
                        + "there to undo. On a concave edge the volume grows, and that is fine. "
                        + "material_change_mm3 gives the change (after minus before); volume_not_read "
                        + "says when the volume could not be read.",
                    Precondition = "solid_body_and_edge",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edges_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "identifiers of edges that ALREADY exist, taken from the document state"),
                            "edges_ref", O(
                                "type", "object",
                                "description", "the edges created by an earlier step of this same plan — for "
                                    + "example «fillet the four corners of the plate you make at step 1». "
                                    + "How many edges there are is known only inside SolidWorks. Give "
                                    + "this OR edges_uid, not both.\n\nAllowed kinds:\n- "
                                    + "bottom_edges: the edges of the border at the bottom of the "
                                    + "extrusion\n- side_edges: the edges along the direction "
                                    + "of extrusion, the vertical ones\n- top_edges: the edges "
                                    + "of the border at the top of the extrusion\n- all_edges: "
                                    + "all the edges created by that step",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bottom_edges",
                                            "side_edges",
                                            "top_edges",
                                            "all_edges"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "radius_mm", O(
                                "type", "number")),
                        "required", A(
                            "radius_mm")),
                },
                new OperationSpec
                {
                    Name = "circular_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Repeats a feature in a circle around an axis or a straight edge.",
                    Description =
                        "Repeats a feature already made, in a circle around an axis. The "
                        + "feature and the axis are indicated with the identifiers taken "
                        + "from the document state.\n\nTHE AXIS CAN BE A STRAIGHT EDGE of the "
                        + "part — the ones the state lists as «E1», «E2» — or a reference "
                        + "axis already in the document.\n\nTo repeat around a HOLE you "
                        + "need an axis that passes through its centre: the edge of the hole "
                        + "is a circle, not a line, and it is refused as a direction. "
                        + "If that axis does not exist yet, make it first with "
                        + "«create_axis».",
                    Precondition = "feature_and_direction",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "what to repeat, if it is already in the tree"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "the feature made by an earlier step of this same plan "
                                    + "— «make a hole and then repeat it 6 times». Give this OR "
                                    + "feature_uid, not both.\n\nAllowed kinds:\n- feature: the "
                                    + "feature made by that step, to repeat it",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis_uid", O(
                                "type", "string",
                                "description", "around what: an axis already in the document, or a STRAIGHT edge "
                                    + "of the part («E1», «E2»...) taken from the state"),
                            "axis_ref", O(
                                "type", "object",
                                "description", "the axis created by an earlier step of this same plan — «make "
                                    + "the axis of the hole and then repeat the feature 6 "
                                    + "times around it». Give this OR axis_uid, not both.\n\nAllowed kinds:"
                                    + "\n- axis: the axis created by the step, from the centre of a hole "
                                    + "or a shaft",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "instance_count", O(
                                "type", "integer"),
                            "total_angle_deg", O(
                                "type", "number",
                                "description", "default: 360")),
                        "required", A(
                            "instance_count")),
                },
                new OperationSpec
                {
                    Name = "linear_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Repeats a feature in a row, or in a grid in two directions: eight "
                        + "holes in a row are made this way.",
                    Description =
                        "Repeats a feature already made, in a row along an axis — or in "
                        + "a grid, along two axes together. It is the twin of "
                        + "«circular_pattern» and is indicated the same way.\n\nEight holes "
                        + "in a row are made THIS WAY, not by calling the hole eight times "
                        + "with eight coordinates: the finished part declares that those "
                        + "holes are a series, and changing their pitch becomes a single "
                        + "dimension.\n\nTHE DIRECTION IS TAKEN FROM A STRAIGHT EDGE of the part: "
                        + "the ones the document state lists as «E1», «E2»... For a row "
                        + "along the plate you indicate the long edge; for a grid, two edges "
                        + "perpendicular to each other.\n\nAn axis made with «create_axis» "
                        + "also works, but that one is born from the centre of a "
                        + "hole and is perpendicular to the drilled face: it is for "
                        + "repeating INTO THE THICKNESS, almost never for a row.\n\n«axis1_uid» "
                        + "is required. «axis2_uid» is only for a grid — a plate drilled in "
                        + "a matrix; for a single row omit it together with pitch2_mm "
                        + "and instance_count2.\n\nThe axis decides the direction, not a "
                        + "sign in the parameters: if the repeat comes out on the wrong "
                        + "side use «flip1»."
                        + "\n\nI REFUSE IF THE DIRECTION COMES FROM A CURVED EDGE: a "
                        + "straight edge is needed. An arc does not have a single direction, and taking "
                        + "one at random would mean a crooked pattern that looks right.",
                    Precondition = "feature_and_direction",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "what to repeat, if it is already in the tree"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "the feature made by an earlier step of this plan. "
                                    + "Give this OR feature_uid, not both.\n\nAllowed kinds:"
                                    + "\n- feature: the feature made by that step, to "
                                    + "repeat it",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis1_uid", O(
                                "type", "string",
                                "description", "direction 1: a STRAIGHT edge of the part («E1», «E2»...) taken from "
                                    + "the state, or an axis already in the document"),
                            "axis1_ref", O(
                                "type", "object",
                                "description", "direction 1, if the axis is created by an earlier step of this "
                                    + "plan with «create_axis». One OR the other.\n\nAllowed kinds:\n- axis: "
                                    + "the axis created by the step, from the centre of a hole or "
                                    + "a shaft",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "pitch1_mm", O(
                                "type", "number",
                                "description", "distance between the instances"),
                            "instance_count1", O(
                                "type", "integer",
                                "description", "how many copies in all, the original included"),
                            "flip1", O(
                                "type", "boolean",
                                "description", "reverses the direction"),
                            "axis2_uid", O(
                                "type", "string",
                                "description", "direction 2, only for a grid"),
                            "pitch2_mm", O(
                                "type", "number",
                                "description", "only with axis2_uid"),
                            "instance_count2", O(
                                "type", "integer",
                                "description", "only with axis2_uid"),
                            "flip2", O(
                                "type", "boolean",
                                "description", "reverses direction 2")),
                        "required", A(
                            "pitch1_mm",
                            "instance_count1")),
                },
                new OperationSpec
                {
                    Name = "fill_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    // true ONLY after the green bench on cases/patterns (the green and the three refusals).
                    Tested = true,
                    Summary =
                        "Fills a flat face with copies of a feature: a plate drilled "
                        + "for lightness, a vent grille.",
                    Description =
                        "Fills a flat face with copies of a feature that already exists: a plate "
                        + "drilled for lightness, a vent grille, a sieve. SOLIDWORKS puts the "
                        + "first copy at the CENTER of the face and adds concentric loops spaced "
                        + "«pitch_mm» apart, with «copies_per_turn» copies on each loop, as long as the "
                        + "face holds them; the ones that would fall outside the outline (shrunk by "
                        + "«margin_mm») are not made. The original stays where it was.\n\nHOW MANY "
                        + "COPIES FIT IS DECIDED BY THE FACE, not by a number you give: the answer counts "
                        + "them in «copies_made». On a 100 x 60 plate with one Ø8 hole, a pitch of "
                        + "about 17 and 10 copies per loop, it is 14.\n\nThree things are needed: "
                        + "the feature («feature_uid» or «feature_ref»), the boundary face it "
                        + "sits on («face_uid» or «face_ref») and a DIRECTION, that is, a straight "
                        + "edge that runs along the face («axis_uid», for example its long edge) "
                        + "or an axis that lies along that face («axis_ref»). The axis of a hole "
                        + "drilled into the face, as «create_axis» makes it, runs across the face and is "
                        + "refused. The direction orients the loops. Measured with an edge of the "
                        + "boundary face; an axis lying along the face is accepted, but no test has used "
                        + "one yet.\n\nFor copies in a row or in a grid use «linear_pattern»; for a circle "
                        + "of copies around an axis, «circular_pattern». The layout is circular only: "
                        + "the square, polygon and perforation layouts are not tested."
                        + "\n\nI REFUSE IF THE DIRECTION IS MISSING: without it SOLIDWORKS creates nothing and does "
                        + "not say why (measured, even though the guide calls it optional). I REFUSE "
                        + "IF THE BOUNDARY FACE IS NOT FLAT, and IF THE DIRECTION IS NOT IN THE "
                        + "PLANE OF THE FACE (an edge of the thickness). And I refuse, removing the pattern, "
                        + "if it comes out with features in error, with the bodies changed, or with "
                        + "copies that bite into the edge or merge with each other.",
                    Precondition = "feature_and_direction",
                    // No Example: the green case uses bench selectors
                    // (@feature, @face, @edge), and an example that the client cannot
                    // copy is a trap (ExamplesTests, entry Z-G1).
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the feature to repeat, if it is already in the tree"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "the feature made by an earlier step of this plan. "
                                    + "Give this OR feature_uid, not both.\n\nAllowed kinds:"
                                    + "\n- feature: the feature made by that step, to "
                                    + "repeat it",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "face_uid", O(
                                "type", "string",
                                "description", "the FLAT face to lay the copies out in, taken from the state "
                                    + "(«F1»...): usually the one the hole starts from"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan. Give "
                                    + "this OR face_uid, not both.\n\nAllowed kinds:\n- "
                                    + "pocket_floor: the floor of the pocket (there only if the pocket is "
                                    + "blind)\n- bottom_face: the face on the sketch plane, "
                                    + "the bottom of the extrusion\n- top_face: the face on "
                                    + "top of the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis_uid", O(
                                "type", "string",
                                "description", "the direction of the loops: a STRAIGHT edge that runs along the "
                                    + "boundary face («E1», «E2»...), for example its long edge, or an axis "
                                    + "already in the document that lies along the face. Always needed: without "
                                    + "it the pattern is not born"),
                            "axis_ref", O(
                                "type", "object",
                                "description", "the direction, when a previous step of this plan makes the axis "
                                    + "with «create_axis»: only an axis that lies along the boundary face works "
                                    + "(for example the axis of a shaft lying on it); the axis of a hole "
                                    + "drilled into the face runs across it and is refused. One OR the "
                                    + "other.\n\nAllowed kinds:\n- axis: the axis made by that step",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "pitch_mm", O(
                                "type", "number",
                                "description", "distance between one loop of copies and the next, center to center"),
                            "copies_per_turn", O(
                                "type", "integer",
                                "description", "how many copies on each loop around the center of the face"),
                            "margin_mm", O(
                                "type", "number",
                                "description", "how far the copies stay from the edge of the face; default 0")),
                        "required", A(
                            "pitch_mm",
                            "copies_per_turn")),
                },
                new OperationSpec
                {
                    Name = "chamfer_edges",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Chamfers one or more edges: deburring, an assembly lead-in, a weld bevel.",
                    Description =
                        "Chamfer on one or more edges. It is the twin of «fillet_edges» "
                        + "and the edges are given the same way.\n\nThe default is 45 "
                        + "degrees, by far the most common chamfer. For a non-symmetric chamfer "
                        + "— a weld bevel, a lead-in that has to enter a seat — give "
                        + "«second_leg_mm» and the angle is ignored.\n\nChamfer or fillet is not "
                        + "an aesthetic preference: a chamfer is made with a milling cutter "
                        + "or a file, a fillet needs a radius tool. If the user says "
                        + "«chamfer», do not make a fillet."
                        + "\n\nI REFUSE IF THE CHAMFER IS TOO BIG FOR THE EDGE -- not even half of it fits. A "
                        + "value over the limit does not always give an error: sometimes a degenerate "
                        + "geometry comes out that passes the rebuild."
                        + "\n\nThe chamfer follows tangent edges like the fillet. The document state "
                        + "lists each chamfer with «selected_edges» and «faces», and its sizes by type: "
                        + "angle-distance «leg1_mm» + «angle_deg», distance-distance «leg1_mm» + "
                        + "«leg2_mm», and a vertex chamfer (made by hand) «vertex_distances_mm», its "
                        + "three distances along the corner's edges. The answer carries «selected_edges» "
                        + "and «faces» too, read the same way."
                        + "\n\nI REFUSE IF the volume of the part does not change after the chamfer is "
                        + "made: the feature is in the tree and worked nothing, and I say it is still "
                        + "there to undo. On a concave edge the volume grows, and that is fine. "
                        + "material_change_mm3 gives the change (after minus before); volume_not_read "
                        + "says when the volume could not be read.",
                    Precondition = "solid_body_and_edge",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edges_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "edges that ALREADY exist, taken from the state"),
                            "edges_ref", O(
                                "type", "object",
                                "description", "the edges created by an earlier step of this plan. Give this OR "
                                    + "edges_uid, not both.\n\nAllowed kinds:\n- bottom_edges: the "
                                    + "edges of the border at the bottom of the "
                                    + "extrusion\n- side_edges: the edges along the extrusion "
                                    + "direction, the vertical ones\n- top_edges: the edges "
                                    + "of the border at the top of the extrusion\n- all_edges: "
                                    + "all the edges created by that step",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bottom_edges",
                                            "side_edges",
                                            "top_edges",
                                            "all_edges"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "leg_mm", O(
                                "type", "number",
                                "description", "how much material is removed, measured on one face"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "default 45; ignored if you give «second_leg_mm»"),
                            "second_leg_mm", O(
                                "type", "number",
                                "description", "only for a non-symmetric chamfer: the leg on the other face")),
                        "required", A(
                            "leg_mm")),
                },
                new OperationSpec
                {
                    Name = "mirror",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Mirrors a feature across one of the three base planes.",
                    Description =
                        "Mirrors a feature that already exists across a plane of symmetry. "
                        + "The feature is given as for «circular_pattern»."
                        + "\n\nThe plane is always one of the three base planes, and it must be "
                        + "stated: getting it wrong does not move the geometry a little, it puts it "
                        + "somewhere else on the part, and the build succeeds all the same.\n\nThe "
                        + "FEATURE is mirrored, not the whole body: for a symmetric part, "
                        + "mirror the hole or pocket that is missing on one side."
                        + "\n\nI REFUSE IF THE PLANE DOES NOT EXIST, and I tell you which ones there are. Invented names "
                        + "-- «diagonal», «oblique» -- I do not interpret: mirroring "
                        + "across the wrong plane produces a plausible and wrong part.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "what to mirror, if it is already in the tree"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "the feature made by an earlier step of this plan. "
                                    + "Give this OR feature_uid, not both.\n\nAllowed kinds:"
                                    + "\n- feature: the feature made by that step, to "
                                    + "repeat it",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right")),
                        "required", A(
                            "plane")),
                },
                new OperationSpec
                {
                    Name = "move_bodies",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Moves or COPIES bodies that already exist, by a given distance.",
                    Description =
                        "Moves the given bodies by «dx_mm», «dy_mm», «dz_mm». With «copy» "
                        + "true it LEAVES THEM WHERE THEY ARE and puts copies further along: "
                        + "this is how two equal frames at a certain distance are made.\n\nIT "
                        + "IS NEEDED WHEN THE PART HAS TWO EQUAL PARTS IN DIFFERENT PLACES. "
                        + "A rack made of two frames 600 mm apart is not asked for by "
                        + "making the frame twice: the second time would be refused, because "
                        + "the same operation twice with the same numbers makes two "
                        + "overlapping bodies. You make ONE frame, and then copy it moved 600 "
                        + "along Z.\n\nWhich bodies: «bodies_uid» for the ones that ALREADY exist "
                        + "in the document (C1, C2...), «bodies_ref» for the ones an "
                        + "earlier step of this same plan creates. One or the other, never both."
                        + "\n\nStraight-line move only: this operation does not rotate "
                        + "anything. If rotating is needed, say so instead of pretending.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the bodies to move, by identifier from the document state "
                                    + "(C1, C2...)"),
                            "bodies_ref", O(
                                "type", "object",
                                "description", "the bodies created by an earlier step of this same plan. "
                                    + "Give this OR bodies_uid, not both.\n\nAllowed kinds:"
                                    + "\n- bodies: the bars created by that step, all together",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "dx_mm", O(
                                "type", "number",
                                "description", "move along X, in mm"),
                            "dy_mm", O(
                                "type", "number",
                                "description", "move along Y"),
                            "dz_mm", O(
                                "type", "number",
                                "description", "move along Z"),
                            "copy", O(
                                "type", "boolean",
                                "description", "true: leaves the originals and adds moved copies. "
                                    + "Default: false, that is, it just moves them."),
                            "copy_count", O(
                                "type", "integer",
                                "description", "how many copies, only if «copy» is true. Default 1. Copy "
                                    + "number k ends up at k times the move.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "cylindrical_cut",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A round cut on a face: for a passage that is NOT a hole "
                        + "for a fastener.",
                    Description =
                        "Makes a hole on an existing face, at the given coordinates in the "
                        + "plane of that face. Through or blind."
                        + "\n\nI REFUSE IF THE HOLE FALLS ENTIRELY OUTSIDE THE MATERIAL (I tell you between which "
                        + "coordinates the face lies) and IF it is so wide that it covers the "
                        + "whole face: that is not a hole, it is a part that disappears.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "identifier of the face, taken from the document state. Use "
                                    + "it for a face that ALREADY exists."),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan, when "
                                    + "the hole goes on geometry you are creating now and which "
                                    + "therefore has no identifier yet. Give this OR face_uid, not "
                                    + "both.\n\nAllowed kinds:\n- pocket_floor: the floor of the "
                                    + "pocket (there only if the pocket is blind)\n- bottom_face: "
                                    + "the face on the sketch plane, the bottom of the "
                                    + "extrusion\n- top_face: the face on top of "
                                    + "the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "MODEL COORDINATES in mm, not face coordinates. Take the two axes "
                                    + "left after removing the one along the face normal, in the "
                                    + "order X, Y, Z: normal along Z -> x_mm is the model X and y_mm "
                                    + "the Y; normal along Y -> x_mm is the X and y_mm the Z; normal along "
                                    + "X -> x_mm is the Y and y_mm the Z. They are the same numbers "
                                    + "as the «point_mm» you read in the document state, so you can "
                                    + "start from those. The third coordinate is not needed: it is the "
                                    + "plane of the face."),
                            "y_mm", O(
                                "type", "number",
                                "description", "see x_mm"),
                            "anchor", AnchorSchema("the hole"),
                            "diameter_mm", O(
                                "type", "number"),
                            "through_all", O(
                                "type", "boolean",
                                "description", "default: yes"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "only if not through")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "diameter_mm")),
                },
                new OperationSpec
                {
                    Name = "rectangular_cut",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A rectangular pocket on a face.",
                    Description =
                        "Cuts a rectangular pocket on an existing face."
                        + "\n\nI REFUSE IF THE POCKET COVERS THE WHOLE FACE, and I tell you how big it is. A "
                        + "pocket as big as the part is not a pocket: it is the part that is no "
                        + "longer there.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "a face that already exists, taken from the state"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this same plan. Give "
                                    + "this OR face_uid, not both.\n\nAllowed kinds:\n- "
                                    + "pocket_floor: the floor of the pocket (there only if the pocket is "
                                    + "blind)\n- bottom_face: the face on the sketch plane, "
                                    + "the bottom of the extrusion\n- top_face: the face on "
                                    + "top of the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "MODEL COORDINATES in mm, not face coordinates. Take the two axes "
                                    + "left after removing the one along the face normal, in the "
                                    + "order X, Y, Z: normal along Z -> x_mm is the model X and y_mm "
                                    + "the Y; normal along Y -> x_mm is the X and y_mm the Z; normal along "
                                    + "X -> x_mm is the Y and y_mm the Z. They are the same numbers "
                                    + "as the «point_mm» you read in the document state, so you can "
                                    + "start from those. The third coordinate is not needed: it is the "
                                    + "plane of the face."),
                            "y_mm", O(
                                "type", "number",
                                "description", "see x_mm"),
                            "anchor", AnchorSchema("the pocket"),
                            "length_mm", O(
                                "type", "number",
                                "description", "along x_mm"),
                            "width_mm", O(
                                "type", "number",
                                "description", "along y_mm"),
                            "through_all", O(
                                "type", "boolean",
                                "description", "default: no"),
                            "depth_mm", O(
                                "type", "number")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "apply_material",
                    Family = "physical_properties",
                    Example = "{\"material\":\"AISI 304\"}",   // from `mass_of_a_steel_cube`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Assigns the material to the part: without it there is no weight and no usable cut list.",
                    Description =
                        "Assigns the material to the part. ALWAYS DO THIS, on every part you "
                        + "draw: without a material there is no density, without density "
                        + "there is no weight, and the cut list comes out with an empty "
                        + "material column — that is, it is no use for buying the steel.\n\nWrite "
                        + "the name as a designer would say it («S235JR», «AISI 304», "
                        + "«6061 Alloy»): the exact spelling is found by the workstation, which "
                        + "has the real libraries. If the name matches more than one "
                        + "material, or none, the operation fails and lists the ones that exist, "
                        + "and then you ask the user: between 1.0037 and 1.0116 you do not "
                        + "choose at random.\n\nIF YOU DO NOT GIVE THE MATERIAL I use "
                        + "«S235JR», which is what this shop works with unless told "
                        + "otherwise, and I write it in the answer, so you know I chose it "
                        + "and you did not."
                        + "\n\nThe material goes on EVERY configuration of the part, each one set "
                        + "and read back by its own name (measured: the empty configuration name "
                        + "reaches only the active configuration and the ones derived from it). The "
                        + "answer lists them in «configurations»; I refuse the step, naming them, if "
                        + "any configuration does not take the material.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "material", O(
                                "type", "string",
                                "default", "S235JR",
                                "description", "for example «S235JR», «AISI 304», «6061 Alloy». If "
                                    + "you do not give it I use S235JR, and I write that in the answer."),
                            "library", O(
                                "type", "string",
                                "description", "optional: the name of the library, if you know which one to search. "
                                    + "Leave it alone if you do not.")),
                        // Z-G3: `material` is NO longer mandatory — there is a
                        // workshop default, and the answer declares that it used
                        // it. A field that is always asked and whose answer is
                        // always the same makes the model write a line that
                        // is not a choice.
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "create_axis",
                    Family = "references",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "An axis along the centre of a hole or a shaft: it is for "
                        + "repeating AROUND that hole.",
                    Description =
                        "Creates a reference axis along the centre of an existing "
                        + "cylindrical or conical face — the hole or shaft around which you then "
                        + "repeat something with «circular_pattern».\n\nWHEN IT IS REALLY "
                        + "NEEDED: to repeat around a HOLE. The edge of a hole is a circle, not "
                        + "a straight line, and it is rejected as a direction; the axis, "
                        + "instead, passes through its centre.\n\nWHEN IT IS NOT NEEDED: for a "
                        + "row or a grid. There the direction is taken from a straight edge "
                        + "of the part, which is already in the state («E1», «E2»...). "
                        + "The axis of a hole points INTO THE THICKNESS of the plate: "
                        + "repeating along that does not make a row.\n\nCylindrical or conical faces "
                        + "only: a planar face has no axis. In the document state they are "
                        + "the ones with «planar: false»."
                        + "\n\nOn a sheet-metal part too: the axis is the one the call made, wherever "
                        + "SOLIDWORKS puts it in the tree (before the flat pattern).",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "the cylindrical or conical face, taken from the document state"),
                            "face_ref", O(
                                "type", "object",
                                "description", "the face created by an earlier step of this plan. Give this OR "
                                    + "face_uid, not both.\n\nAllowed kinds:\n- pocket_floor: the "
                                    + "floor of the pocket (it exists only if the pocket is blind)\n"
                                    + "- bottom_face: the face on the sketch plane, the bottom "
                                    + "of the extrusion\n- top_face: the face at the top of "
                                    + "the extrusion, the one opposite the sketch plane",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind"))),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "create_plane",
                    Family = "references",
                    Example = "{\"plane\":\"front\",\"distance_mm\":-250}",   // from `front_plane_minus_250`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A new reference plane, parallel to a base plane and "
                        + "offset by a distance.",
                    Description =
                        "Creates a reference plane parallel to a base plane (front, "
                        + "top or right) and offset by a distance along its normal. "
                        + "The base plane is found by its geometry (its normal through the "
                        + "origin), not by its place in the tree, so a template with an extra plane "
                        + "before the three default ones does not move it. "
                        + "It is for building a sketch at a certain height or depth "
                        + "without touching the base plane — «a plane parallel to top, at "
                        + "600 mm» to work at half the height of an upright.\n\nNegative "
                        + "distance: on the opposite side. Zero distance: a plane "
                        + "coincident with the base, as a separate object.\n\nBase planes only: "
                        + "this version cannot rest the new plane on a face "
                        + "of the part.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "base plane: front, top or right"),
                            "distance_mm", O(
                                "type", "number",
                                "description", "how far it is offset from the base plane. Negative for the other direction.")),
                        "required", A(
                            "distance_mm")),
                },
                new OperationSpec
                {
                    Name = "weld_bead",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Cosmetic weld bead between two faces of two bars that meet at an angle.",
                    Description =
                        "Puts a cosmetic fillet weld bead between TWO faces of two bodies "
                        + "that touch along an edge and form an angle: for one bar butted against "
                        + "another, the SIDE face of the bar and the face it arrives on. "
                        + "It neither removes nor adds material: it documents the weld, "
                        + "and it ends up in the drawings and in the bill of materials.\n\nONE "
                        + "PAIR OF FACES PER CALL. A bead that runs all round a rectangular "
                        + "tube is several calls, one per side: with more than one face per "
                        + "side SOLIDWORKS creates nothing. The faces are given by identifier, "
                        + "taken from the document state: this is an operation you "
                        + "ask for AFTER the bars and the cuts are made, by looking at them."
                        + "\n\nI REFUSE IF the two faces do not touch, or are parallel: SOLIDWORKS "
                        + "would create the bead all the same, and the bill of "
                        + "materials would end up with a weld that does not exist. The length I "
                        + "return is that of the edge they have in common.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "start_face_uid", O(
                                "type", "string",
                                "description", "a face of the first body, taken from the document state "
                                    + "(F1, F2...)"),
                            "end_face_uid", O(
                                "type", "string",
                                "description", "a face of the OTHER body, which touches the first one along an "
                                    + "edge and makes an angle with it"),
                            "leg_mm", O(
                                "type", "number",
                                "description", "side of the bead (a)")),
                        "required", A(
                            "start_face_uid",
                            "end_face_uid",
                            "leg_mm")),
                },
                new OperationSpec
                {
                    Name = "cut_list",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Creates or updates the cut list of the weldment part. Goes last.",
                    Description =
                        "Creates or updates the CUT LIST of the weldment part: the list of "
                        + "bars with profile, size, length and quantity.\n\nIt is the document "
                        + "you take to the workshop, and it is the reason a frame is made with "
                        + "structural members instead of extrusions. Call it AFTER "
                        + "you have placed all the bars and made all the cuts: a cut list "
                        + "calculated earlier reports lengths that change afterwards.\n\nONE ITEM "
                        + "FOR EACH GROUP OF IDENTICAL BARS: the part becomes a weldment (the «Weldment» "
                        + "feature, named in the language of the installation, if it is missing) "
                        + "and the cut list is the automatic one of SOLIDWORKS, which separates the bars "
                        + "by length. The answer carries the items (description, length, quantity), "
                        + "and the sum of the quantities must be the number of bars, otherwise it says so. "
                        + "The gussets and the end caps are in the list too, one item for each group of "
                        + "identical ones: they have no description and no length, so every item says its "
                        + "item_type (weldment_member, solid_body or sheet_metal) and names its first_body."
                        + "\n\nThe answer says what SOLIDWORKS answered: automatic_cut_list_set (null "
                        + "when the cut list was automatic already), automatic_update_set and "
                        + "cut_list_updated. When the cut list did not update, the warning field of the "
                        + "answer says the numbers may be stale. Each item gets bodies_equal, and the "
                        + "same warning field speaks when "
                        + "an item QUANTITY differs from the bodies of its folder, when one item holds "
                        + "bodies of different volume, or when the folders hold a different number of "
                        + "bodies than the part. A cut list that is not automatic (made by hand or "
                        + "before library 0.124.0) is refused: delete it first. When a refusal removes "
                        + "the weldment feature this step added, the message says whether IsWeldment "
                        + "reads false again.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "update_if_exists", O(
                                "type", "boolean",
                                "description", "if the cut list already exists, it recalculates it instead of creating "
                                    + "a second one (default: yes)")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "gusset",
                    Family = "weldments",
                    RequestedDocument = "part",
                    // ── PROVEN SINCE 28/9/2026 (entry GP20) ─────────────
                    //
                    // Up to library 0.139.0 a gusset was never born:
                    // the method passed a polygon with the short sides at zero
                    // (ProfileD3 = ProfileD4 = 0) and no mark, and
                    // InsertGussetFeature3 returned null. The API guide (Remarks
                    // of InsertGussetFeature3, Insert_Gusset_Feature_Example_
                    // CSharp.htm) wants the two faces with mark 1 AND a valid
                    // profile. Measured with the probe Checks\Gusset\
                    // Program.cs on the T 80x80x5: triangle 60/60
                    // thickness 5 +9000 mm3, polygon 60/60/15/15 +12937.5, bodies
                    // 2 -> 3. Cases: weldments/gusset_sits_in_the_corner and
                    // weldments/gusset_polygon_is_not_degenerate.
                    Tested = true,
                    Summary =
                        "Reinforcing gusset, triangular or polygonal, in the corner between two "
                        + "planar faces of two bars that meet.",
                    Description =
                        "Puts a reinforcing GUSSET in the corner between two bars of a welded "
                        + "frame: a plate, a body of its own, that stiffens the joint and ends up in the "
                        + "cut list.\n\nTHE TWO FACES are given by identifier, taken from the "
                        + "document state: this is an operation you ask for AFTER the bars and the cuts "
                        + "are made, by looking at them. They must be PLANAR, make an angle and "
                        + "touch along an edge: for one bar butted against another, the SIDE face "
                        + "of the bar and the face it arrives on (the same ones as for "
                        + "«weld_bead»). At a corner of rectangular tubes there are four candidate "
                        + "faces, and picking two «likely» ones would mean putting the reinforcement "
                        + "on the wrong side. Parallel, distant or curved faces: I refuse, before "
                        + "touching the part.\n\nTHE PROFILE: «triangular» (the default) with the "
                        + "two sides «leg1_mm» and «leg2_mm» along the two faces; or «polygonal», "
                        + "which cuts off the tip and also needs the two short sides «side3_mm» "
                        + "and «side4_mm», shorter than the two legs. A polygon without short "
                        + "sides is degenerate and I refuse it: it is the defect because of which "
                        + "this operation never created anything until library 0.139.0. The thickness "
                        + "lies on both sides of the gusset's plane; «position» is «start», «center» "
                        + "(the default) or «end» along the common edge, and I refuse any other value "
                        + "instead of quietly putting it in the center.\n\nTHE ANSWER IS READ "
                        + "BACK, not repeated: the added volume against the hand calculation "
                        + "(«volume_added_mm3», «volume_expected_mm3»), the bodies before and after, "
                        + "the thickness, the profile, the position and the two supporting faces "
                        + "read back from the feature («thickness_read_mm», «profile_read», «location_read», "
                        + "«supporting_faces_area_mm2»), the new body and "
                        + "its box («new_body», «new_body_box_mm»). If one of these does not "
                        + "match, I remove the gusset and tell you. If you only need to document "
                        + "the weld, use «weld_bead».",
                    // No example: the two green cases name the faces with the
                    // bench selectors (@face(area~=...,normal=...)), which the
                    // client cannot copy (see OperationSpec.Example).
                    Example = null,
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faces_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "exactly TWO planar faces, one per bar, that touch along an "
                                    + "edge and make an angle, taken from the document state (F1, F2...)"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "thickness of the plate, on both sides of its plane"),
                            "leg1_mm", O(
                                "type", "number",
                                "description", "one of the two sides of the gusset, the ones that run along the two "
                                    + "faces. Which side goes on which face the guide does not say, and no test has "
                                    + "told them apart (measured only with equal sides): if it matters to you, look "
                                    + "at the part afterwards"),
                            "leg2_mm", O(
                                "type", "number",
                                "description", "the other side, along the other face (see leg1_mm)"),
                            "profile", O(
                                "type", "string",
                                "enum", A(
                                    "triangular",
                                    "polygonal"),
                                "description", "shape of the plate (default: triangular). The polygonal one cuts "
                                    + "off the tip and needs «side3_mm» and «side4_mm»"),
                            "side3_mm", O(
                                "type", "number",
                                "description", "only with the polygonal profile: first short side, shorter than the two legs"),
                            "side4_mm", O(
                                "type", "number",
                                "description", "only with the polygonal profile: second short side, shorter than the two legs"),
                            "position", O(
                                "type", "string",
                                "enum", A(
                                    "start",
                                    "center",
                                    "end"),
                                "description", "where it sits along the edge between the two faces (default: center)")),
                        "required", A(
                            "faces_uid",
                            "thickness_mm",
                            "leg1_mm",
                            "leg2_mm")),
                },
                new OperationSpec
                {
                    Name = "structural_member",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Real metalwork bars along the segments you give: this is "
                        + "what generates the cut list.",
                    Description =
                        "Creates one or more BARS of a welded frame: real structural "
                        + "members, with the profile taken from the company library.\n\nIT "
                        + "IS THE OPERATION TO USE FOR EVERYTHING THAT IS METALWORK: "
                        + "tubes, angles, flat bars, IPE, HEA, HEB, UPN, UPE, U and T sections "
                        + "— whether a single bar or a whole frame.\n\nWhy extruding a "
                        + "section is not enough: only the structural member generates the CUT "
                        + "LIST, carries the name of the profile, and can be mitered "
                        + "at 45 degrees at the joints. An extruded bar has the same shape "
                        + "and is no use for making the part.\n\nFIRST CALL "
                        + "«weldment_profiles» and copy «profile_path» and "
                        + "«configuration» from there to the letter. Do not compose the name of "
                        + "the size: the library is not uniform and an invented name "
                        + "makes SolidWorks use another size, without an error.\n\nThe segments "
                        + "are the lines along which the bars run, in model coordinates "
                        + "in mm. A straight bar 200 long on the X axis is one segment "
                        + "only: from [0,0,0] to [200,0,0]. A rectangular frame is four "
                        + "segments that close."
                        + "\n\nIf the weldment profile library is still open and modified in "
                        + "SOLIDWORKS after the insertion, the answer carries "
                        + "«profile_library_warning»: close that library without saving, or a "
                        + "Ctrl+S would rewrite the shared library. The warning can carry a second "
                        + "sentence: the library is still LOADED, hidden (an open document references it, or the close did nothing), "
                        + "with the insertion's changes in memory even if its save flag "
                        + "reads clean."
                        + "\n\nI REFUSE IF «configuration» is empty and the profile file has several "
                        + "sizes, and I list them: with an empty size SOLIDWORKS builds the size the file "
                        + "was saved on (measured: the 20 x 20 of the four in iso\\square tube.sldlfp). "
                        + "An empty size is accepted only on a file with one size, and the answer says so "
                        + "in «configuration_note».",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profile_path", O(
                                "type", "string",
                                "description", "full path of the .sldlfp, copied from weldment_profiles"),
                            "configuration", O(
                                "type", "string",
                                "description", "the size, copied TO THE LETTER from weldment_profiles (e.g. "
                                    + "«80X80X5»: upper case and commas included). Empty only for a profile "
                                    + "file with one size."),
                            "segments", O(
                                "type", "array",
                                "description", "the lines along which the bars run, in mm in model coordinates",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "start", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] of the start, in mm"),
                                        "end", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] of the end, in mm")),
                                    "required", A(
                                        "start",
                                        "end"))),
                            "rotation_deg", O(
                                "type", "number",
                                "description", "rotation of the profile about its own axis (default: 0). "
                                    + "Needed when an angle or a U has to be turned."),
                            "corner_treatment", O(
                                "type", "string",
                                "enum", A(
                                    "none",
                                    "miter",
                                    "butt1",
                                    "butt2"),
                                "description", "how the bars of THIS group meet each other, WHERE THEY TOUCH. "
                                    + "«miter» is the 45 degree cut at the corners (default: "
                                    + "miter, the 45 degree miter, wherever the bars of "
                                    + "this group touch, closed or open path; the answer says it was the "
                                    + "default in «corner_treatment_note»); «butt1»/«butt2» is one bar "
                                    + "butted against the other; «none» leaves them interpenetrating "
                                    + "and should be said only if you really want that.\n\nIt does "
                                    + "NOT cut the FREE ends. On a single bar, or at the two ends "
                                    + "of an open frame, there is nothing to cut against and this "
                                    + "parameter has no effect."),
                            "gap_mm", O(
                                "type", "number",
                                "description", "gap between the bars at the joints, for the weld bead "
                                    + "(default: 0)")),
                        "required", A(
                            "profile_path",
                            "configuration",
                            "segments")),
                },
                new OperationSpec
                {
                    Name = "trim_members",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Trims or extends the bars where they meet.",
                    Description =
                        "Trims the bars where they meet, or extends them until they touch. "
                        + "It is the «trim/extend» of the weldments toolbar.\n\nNeeded "
                        + "when two groups of bars cross and have not been treated at the "
                        + "corners: without it the bodies stay interpenetrating and the "
                        + "cut list reports lengths that do not match in the workshop.\n\nI "
                        + "return how much the volume changed (negative = removed). I REFUSE "
                        + "IF the part does not change: that means the bars do not meet, "
                        + "and I do not confirm a cut that cuts nothing."
                        + "\n\nA bar that stops short of the other one is extended to its face "
                        + "(measured: a bar 20 mm short grows by 20 mm, Options 1|2|8); a bar that "
                        + "enters the other is cut at its face as before. The coped cut is not used.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_to_trim_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the bars to trim or extend (a bar that stops short grows to "
                                    + "the other one), by the identifier of the document state (C1, "
                                    + "C2...). Use this for bodies that exist ALREADY; the other field for "
                                    + "bodies a previous step of this plan creates. One or the other, never "
                                    + "both."),
                            "bodies_to_trim_ref", O(
                                "type", "object",
                                "description", "the bars to trim or extend, made by a previous step\n\nKinds "
                                    + "allowed:\n- bodies: the bars created by that step, all together",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "trim_boundary_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the bars to cut against, by identifier taken from the "
                                    + "document state (C1, C2...). Use this for bodies that ALREADY exist; "
                                    + "the other field is for bodies that a previous step of this "
                                    + "plan creates. One or the other, never both."),
                            "trim_boundary_ref", O(
                                "type", "object",
                                "description", "the bars to cut against, from a previous step\n\nKinds "
                                    + "allowed:\n- bodies: the bars created by that step, all together",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "end_condition", O(
                                "type", "string",
                                "enum", A(
                                    "miter",
                                    "butt1",
                                    "butt2",
                                    "cut"),
                                "description", "how the cut ends (default: cut). Any other word is "
                                    + "refused before the part is touched, a word meaning no "
                                    + "corner included: the trim/extend has no such corner type, and none "
                                    + "was measured. The answer names the condition used in end_condition."),
                            "gap_mm", O(
                                "type", "number",
                                "description", "gap left (default: 0). The gap you pass is always "
                                    + "the one applied, even 0: the last gap given by hand "
                                    + "in SOLIDWORKS is not inherited")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "end_cap",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Closes the open ends of hollow bars with a cap.",
                    Description =
                        "Closes the open ends of a hollow bar with a cap. It goes on "
                        + "the end faces created by a previous step, or on the bars that are "
                        + "already in the document («C1», «C2»...).\n\nTHE ONLY THING "
                        + "THAT MAY BE MISSING IS THE THICKNESS OF THE CAP. It has no "
                        + "default, because it is a design choice: ask for it, and ask "
                        + "ONLY for that. The material is not needed — it is applied afterwards "
                        + "with its own operation, and it does not change the shape of "
                        + "the cap."
                        + "\n\nA PLANAR FACE WITH A HOLE MACHINED INTO IT IS NOT AN OPEN END. "
                        + "A drilled side wall of a tube (its outer and its inner face) is "
                        + "looked at and left out, and the answer counts it in "
                        + "machined_faces_skipped. An open end is a face whose void was made "
                        + "by the bar itself, so an end whose inner edges were chamfered or "
                        + "counterbored is left out the same way. When a face cannot be told "
                        + "apart, nothing is capped and the answer says why.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the bars whose open ends are to be capped, by identifier taken "
                                    + "from the document state (C1, C2...). Use this for bodies that "
                                    + "ALREADY exist; the other field is for bodies that a previous "
                                    + "step of this plan creates. One or the other, never both."),
                            "ends_ref", O(
                                "type", "object",
                                "description", "the end faces to cap, from a previous step\n\nKinds allowed:\n- "
                                    + "free_ends: the end faces of the bars of that step that "
                                    + "are still open, the ones that get capped (a "
                                    + "wall with a hole machined into it is not an end: it is left out)",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the sequence number of the step that creates the geometry, counting "
                                            + "from 1. It must be a step BEFORE this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "free_ends"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "thickness of the cap"),
                            "inset_mm", O(
                                "type", "number",
                                "description", "NOT a depth: the side gap between the cap and the inside of "
                                    + "the tube wall, on every side. The cap always sits flush with the end "
                                    + "of the bar. 0 or absent: half the wall thickness per side (measured on "
                                    + "an 80 x 80 x 5 tube, 70 x 70 inside: 65 x 65 caps); a value above 0 is "
                                    + "that gap. A cap that fills the whole inside with no gap cannot be asked "
                                    + "for yet."),
                            "chamfer_mm", O(
                                "type", "number",
                                "description", "IGNORED today: the corner treatment of the cap is off, and "
                                    + "without it SOLIDWORKS drops the chamfer (measured: 2 mm gives the same "
                                    + "volume as none). The cap corners stay sharp whatever the value.")),
                        "required", A(
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "rectangular_frame",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A rectangular frame of four bars, given with two dimensions "
                        + "instead of eight coordinates.",
                    Description =
                        "RECIPE: a flat rectangular frame, four bars welded at the corners. "
                        + "It is the most common case of metalwork, and it is asked for with "
                        + "two dimensions instead of eight coordinates.\n\nThe dimensions "
                        + "are the OVERALL OUTSIDE SIZE of the finished frame. The plane "
                        + "says which principal plane it lies on.\n\nAs for «structural_member», "
                        + "the profile and the size are copied from «weldment_profiles»."
                        + "\n\nI REFUSE IF the profile does not fit in "
                        + "the two sizes (its overhang, measured on a trial frame, is as large "
                        + "as a side), or if «measured_as» is neither «overall» nor "
                        + "«center_distance» (upper or lower case does not matter), and I draw nothing. The answer "
                        + "says which reading it used («measured_as»), whether the profile's overhang was "
                        + "measured now or remembered from an earlier frame («overhang_source»: "
                        + "«measured» or «archive»), and, if the weldment profile library is "
                        + "still open and modified in SOLIDWORKS afterwards, "
                        + "«profile_library_warning»: close that library without saving. The warning "
                        + "or the refusal can also say that the library is still LOADED, hidden, "
                        + "because an open document references it or the close did nothing, with the changes in memory even "
                        + "if its save flag reads clean.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profile_path", O(
                                "type", "string"),
                            "configuration", O(
                                "type", "string",
                                "description", "the profile size, copied from weldment_profiles. Empty is "
                                    + "accepted only on a profile file with one size; with several sizes it is "
                                    + "refused, and the sizes are listed."),
                            "length_mm", O(
                                "type", "number",
                                "description", "overall outside size"),
                            "width_mm", O(
                                "type", "number",
                                "description", "overall outside size"),
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "construction plane: front, top or right"),
                            "corner_treatment", O(
                                "type", "string",
                                "enum", A(
                                    "miter",
                                    "butt1",
                                    "butt2",
                                    "none"),
                                "description", "default: miter (45 degree miter); the answer says when "
                                    + "it was the default («corner_treatment_note»)"),
                            "rotation_deg", O(
                                "type", "number"),
                            "gap_mm", O(
                                "type", "number"),
                            "measured_as", O(
                                "type", "string",
                                "enum", A(
                                    "overall",
                                    "center_distance"),
                                "description", "what the two dimensions mean: «overall» is the size of the "
                                    + "finished part from outside to outside (it is what a "
                                    + "designer means by «frame 1000x800»); «center_distance» is the distance "
                                    + "between the axes of the bars. Default: overall. "
                                    + "Upper or lower case does not matter («Overall» is "
                                    + "«overall»); any other word is refused."),
                            "centered", O(
                                "type", "boolean",
                                "description", "frame centered on the origin (default: yes)")),
                        "required", A(
                            "profile_path",
                            "configuration",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "create_drawing",
                    Family = "drawing",
                    Example = "{\"paper_size\":\"A3\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Opens a new drawing of the part you are looking at.",
                    Description =
                        "Creates the DRAWING document of the open part and brings it to the "
                        + "front. The part stays open: a drawing view attaches to the "
                        + "PATH of an open document, not to a file.\n\nTHE PART MUST ALREADY "
                        + "BE SAVED. If it is not, save it first with «save_document»."
                        + "\n\nThe drawing template is NOT chosen: the one of this "
                        + "installation is used, which carries the shop's title block "
                        + "and the right projection angle. From here on the active document "
                        + "is the DRAWING, and the operations that model the part no "
                        + "longer apply until you go back to the part.",
                    Precondition = "a part or an assembly SAVED to disk",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "paper_size", O(
                                "type", "string",
                                "enum", A(
                                    "A4",
                                    "A4V",
                                    "A3",
                                    "A2",
                                    "A1",
                                    "A0"),
                                "description", "sheet size; A3 if you say nothing")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "export_pdf",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "The PDF of ALL the sheets, with the pages counted.",
                    Description =
                        "Writes the PDF of the drawing.\n\nALL the sheets, not only the one "
                        + "you are looking at: the standard export takes the current "
                        + "sheet, and a three-sheet drawing reaches the shop as a single "
                        + "page without any error. After writing the file the pages are "
                        + "COUNTED and compared with the sheets: if they do not match, I "
                        + "tell you.\n\nI REFUSE IF SOLIDWORKS does not accept "
                        + "the request for every sheet (IExportPdfData.SetSheets answers false), "
                        + "before writing anything; and when the pages of the written file "
                        + "cannot be counted, saying where the file is: without that count, the "
                        + "check this operation exists for did not run.\n\nThe answer carries "
                        + "save_warnings (the swFileSaveWarning_e names, or «none») and "
                        + "save_warnings_note when the drawing needed a rebuild, had rebuild "
                        + "errors, or had views needing an update. A failed save gives the error "
                        + "and warning names next to the raw codes.\n\nWith «search» the PDF is READ BACK: tell me the texts that "
                        + "must be there (the drawing number, the part name) and I tell "
                        + "you which are there («found») and which are not "
                        + "(«not_found»). The text in the PDF is written as glyph indices, "
                        + "and a search in the bytes does not find it: here it is decoded.",
                    Precondition = "an open drawing",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the PDF. If you do not say, next to the drawing, with "
                                    + "the same name."),
                            "search", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "texts the PDF must contain: the file is read back and I say which ones are there")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "set_sheet",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Sheet size and scale.",
                    Description =
                        "Changes the SIZE and the SCALE of the active sheet, and reads "
                        + "back to check.\n\nTHE PROJECTION ANGLE IS NOT TOUCHED and is not a parameter. "
                        + "A drawing in third-angle projection has the right and left "
                        + "views swapped, gives no error, and the part that comes out of it "
                        + "is mirrored: what the shop's drawing template has already "
                        + "decided is read and written back unchanged. You find which one it is in "
                        + "the measurements.",
                    Precondition = "an open drawing",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "paper_size", O(
                                "type", "string",
                                "enum", A(
                                    "A4",
                                    "A4V",
                                    "A3",
                                    "A2",
                                    "A1",
                                    "A0"),
                                "description", "sheet size"),
                            "scale_numerator", O(
                                "type", "number",
                                "description", "the first number of the scale: the 1 in «1:2»"),
                            "scale_denominator", O(
                                "type", "number",
                                "description", "the second number of the scale: the 2 in «1:2»")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "model_dimensions",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Brings onto the drawing the dimensions the part already has.",
                    Description =
                        "Takes the dimensions from the MODEL and puts them on the views.\n\n"
                        + "The import works from this bridge, out of process: measured on 26/9/2026 (7 "
                        + "dimensions on a plate) and again on 28/9/2026. To put on the drawing "
                        + "a dimension the model does not have, «drawing_dimension» CREATES it on "
                        + "the edges the view shows.\n\nEach model dimension comes in once: I "
                        + "ask SOLIDWORKS to eliminate duplicates (DuplicateDims true), so a "
                        + "dimension already shown in one view is not repeated in another "
                        + "(measured: 4 dimensions on a plate that got 7 before).\n\nWhy it "
                        + "exists all the same: a dimension that comes from the model cannot "
                        + "carry a number different from the geometry, while the SolidWorks "
                        + "API lets you write on the drawing a text the geometry does not have."
                        + "\n\nBy default only the dimensions "
                        + "MARKED for the drawing come in, that is, the ones chosen to be shown: "
                        + "with «all_dimensions» the others come in too, and the drawing "
                        + "becomes quite a lot denser.\n\nBy default only the dimensions MARKED for "
                        + "drawing come in, fallbacks included. If the part has dimensions and "
                        + "none is marked, I refuse and say so: pass all_dimensions=true to "
                        + "import them all, or mark on the model the ones the drawing must show."
                        + "\n\nI REFUSE IF the drawing has no views (the dimensions "
                        + "would have nothing to attach to) and if no dimension comes in: I tell you, "
                        + "with how many the part has marked, instead of answering «done».",
                    Precondition = "a drawing with at least one view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "all_dimensions", O(
                                "type", "boolean",
                                "description", "also the dimensions not marked for the drawing. It makes the drawing "
                                    + "denser: use it when the marked ones are not enough. Without "
                                    + "it, a part where no dimension is marked for drawing is refused, not "
                                    + "imported."),
                            "hole_callouts", O(
                                "type", "boolean",
                                "description", "also the annotations of Hole Wizard holes (M8, depth, thread). "
                                    + "On if you say nothing.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "center_marks",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    // ── PROVEN SINCE 26/9/2026 (library 0.125.0) ──────────
                    //
                    // Since 17/9 it was `Tested = false`: `AutoInsertCenterMarks2`
                    // answered True and put zero marks in 25 combinations, and
                    // `InsertCenterMark2/3` on the selected edges zero. On
                    // 26/9 (the historical probe of 0.89, case «center crosses 2», with the positive check):
                    // the marks on the holes WERE THERE (SOLIDWORKS puts them when creating the
                    // view) and `GetCenterMarkCount` did not count them; from code a new
                    // mark is not born. The operation is a check: it counts
                    // by enumerating and refuses the holes without a mark. See entry W2.11.
                    Tested = true,
                    Summary =
                        "Checks that every hole in the views has its center mark, really counting them, and refuses if one is missing.",
                    Description =
                        "CHECKS that every hole visible in the views has its center mark, and tries "
                        + "to add it where it is missing. SOLIDWORKS puts them in by itself when it creates "
                        + "the view, if the document option that puts center marks on the holes of a new view "
                        + "(Detailing, automatic insertion on view creation) is on: those holes I recognise "
                        + "(«circles_already_marked»). A new mark cannot be made from code (measured): a "
                        + "hole without a mark is a refusal, with the way out — the option on and the view rebuilt.\n\nWHY IT "
                        + "IS NEEDED. A drawing with twelve holes and no center mark prints and looks "
                        + "complete. In the shop, the thing needed to drill is missing: "
                        + "**where the centre is.**\n\nI TELL YOU HOW MANY WERE BORN, counted on the view: "
                        + "every mark must raise the count by one, and a circle left without a mark "
                        + "is a refusal. If the number is **zero** I tell you instead of saying «done» "
                        + "— almost always the view is the wrong one (holes seen as circles are "
                        + "seen from above, usually V2).\n\nTHE SIZE, if you do not tell me, "
                        + "is the document's: it is a house convention.\n\nWithout «view» I look "
                        + "at all of them. Not fillets or slots: measured, on arcs the mark is not "
                        + "born from code.\n\nCENTER MARKS ARE COUNTED BY ENUMERATING THEM: the "
                        + "SOLIDWORKS counter (GetCenterMarkCount) does not see the ones put in "
                        + "when the view is created, and said zero on a sheet that had three."
                        + "\n\nThe walk over the holes is the one standard_views also uses on views "
                        + "left with no mark.",
                    Precondition = "a drawing with at least one view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "ONE view, as the state of the drawing calls it (V1, V2...) "
                                    + "or by name. Empty = all the views."),
                            "applies_to", O(
                                "type", "string",
                                "enum", A("holes"),
                                "description", "«holes», the whole circles (the only value accepted: «fillets» is refused, because on a fillet arc the mark is not born from code, measured)."),
                            "size_mm", O(
                                "type", "number",
                                "description", "how big the mark is. If you do not say, I use "
                                    + "the document's settings, which is almost always the right thing."),
                            "extended_lines", O(
                                "type", "boolean",
                                "description", "the axis lines that go out from the mark to the edge "
                                    + "of the circle. Off if you say nothing.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_dimension",
                    Family = "drawing",
                    Example = "{\"edges\":[0,1,2,3]}",   // from `drawing_dimensions_itself`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND WHAT THAT MEANS HERE ───────────────
                    //
                    // Measured on 18/9/2026 out of process: four dimensions on
                    // four edges, and the VALUES READ BACK with
                    // `GetSystemValue2` — 120 mm the plate, 5 mm the chamfer, on
                    // a 120 x 80 x 10 part with chamfer 5.
                    //
                    // It is not enough that they appear: a dimension that measures something else
                    // is worse than a dimension that is missing, because on
                    // paper it reads the same. For this the operation reads back
                    // every dimension and returns the millimetre, and for this the
                    // first run of the probe — which answered `?` to the value —
                    // would NOT have been enough to mark it proven.
                    Tested = true,
                    Summary =
                        "Puts dimensions on the edges of a view, and READS BACK how much they measure.",
                    Description =
                        "Puts reference dimensions on the edges of a drawing view, and for each "
                        + "one tells you **how many millimetres it measures**, read back "
                        + "from SOLIDWORKS after placing it.\n\nTHIS IS THE OPERATION THAT "
                        + "DIMENSIONS. «model_dimensions» does something else: it IMPORTS "
                        + "the dimensions the part already has, and on this installation it "
                        + "brings in none (measured on eight different drawing templates). "
                        + "If you need a dimensioned drawing, use this one.\n\nTHE EDGES "
                        + "HAVE A NUMBER, and I give it to you: the view exposes its list, and "
                        + "without «edges» I dimension the first «count». Ask me for an "
                        + "edge that does not exist and I answer how many the view has, instead "
                        + "of dimensioning another one.\n\nI TELL YOU HOW MANY WERE BORN, "
                        + "not that I called the API: the dimensions are counted before and "
                        + "after, one by one. If none is born I tell you, and I tell you which edges I tried."
                        + "\n\nWHERE THEY END UP: outside the view — the "
                        + "horizontal ones below, the vertical ones at the side, inside the sheet frame "
                        + "— from the smallest to the largest."
                        + "\n\nOVERLAPS ARE CHECKED AND REPORTED, not avoided: the true rectangle of "
                        + "every dimension text, read back from SOLIDWORKS, against the other texts of "
                        + "the whole drawing and against the lines of the part in this view. "
                        + "«overlapping_texts» lists each overlap, and the answer also gives "
                        + "their count and how many texts were read; null, not 0, means the texts "
                        + "could not be read, or the walk over the dimensions of the drawing broke off "
                        + "before the end, so overlaps are unknown. Dimension lines, "
                        + "extension lines and the title block are not checked. The dimensions are "
                        + "correct; a well laid out drawing is still a trade."
                        + "\n\nI DIMENSION HOLES AND CHAMFERS BY MYSELF, in every view that shows them, even if "
                        + "you do not ask: holes with the HOLE CALLOUT («4× Ø8 THRU», with the word for «through» "
                        + "that SOLIDWORKS writes in the language of the installation, one callout per diameter AND depth: two through Ø8 and two blind Ø8 get two "
                        + "callouts, never one «4×») and the position from the two "
                        + "reference edges, the left one and the bottom one; chamfers with the CHAMFER "
                        + "DIMENSION («5 x 45°»), not with their edges — the hypotenuse and the "
                        + "segments shortened by the chamfer are not dimensioned. A blind hole "
                        + "carries the depth («Ø8 ↧5»); if the callout is not born, I write it myself from the geometry."
                        + "\n\nTHE «4×» IS READ BACK: after writing the count in front of the "
                        + "callout I read its prefix again (holes[].prefix_read). If the count did "
                        + "not stick, the holes are listed in edges_not_dimensioned and the count "
                        + "must be written by hand; «how_many» is the number of holes, not the text."
                        + "\n\nA CIRCLE IS NOT ALWAYS A HOLE: the outline of a round "
                        + "part seen from the front has a diameter dimension (outer_diameters)."
                        + "\n\nA ROUND PART SEEN FROM THE SIDE (shaft, bushing, pin) I dimension "
                        + "like this: the LENGTHS between faces, in parallel from the face "
                        + "of one end (round_lengths_mm), and the DIAMETERS from the two generatrices "
                        + "of each cylinder, with the Ø (round_diameters_mm), each one read back. "
                        + "SOLIDWORKS does not always expose the generatrices of a cylinder: those "
                        + "diameters I tell you in round_diameters_not_reached, and they must be "
                        + "dimensioned by hand (an internal hole is dimensioned from the view in which it is a circle)."
                        + "\n\nONLY ONE DIMENSION PER FEATURE "
                        + "ON THE WHOLE DRAWING: if the same extent of the part is already "
                        + "dimensioned in another view, here I skip it and tell you (UNI ISO 129-1). So "
                        + "dimension the views one at a time, and do not expect in each view the measurements another one already has."
                        + "\n\nON EVERY SHEET: «already dimensioned» is read from the views of "
                        + "all the sheets, only from views of the same part and configuration, "
                        + "and a dimension hidden by hand does not count (one on a hidden layer, "
                        + "or of a suppressed feature, still does: I cannot see those). The "
                        + "answer says what was read: already_dimensioned_read_from («every "
                        + "sheet», or «current sheet only» when the drawing did not hand over "
                        + "its sheets), hidden_dimensions_not_counted, "
                        + "views_of_other_models_not_counted."
                        + "\n\nA DIMENSION BORN WITHOUT ITS OBJECT (the count of dimensions went up, "
                        + "the call returned nothing) stays: it is listed with its values null and "
                        + "named in edges_not_dimensioned, since its value was not read back. For a hole "
                        + "it does not trigger the fallback diameter dimension, which would be a second "
                        + "annotation on the same hole. The same holds in the turned view: a length or "
                        + "a diameter born without its object is not in round_lengths_mm or "
                        + "round_diameters_mm (they list what was read back), is not dimensioned again in "
                        + "another view and is not reported as not reached; one born between two "
                        + "silhouette edges without a face cannot be matched to a cylinder, so no further "
                        + "pair is tried, and its line in edges_not_dimensioned says so."
                        + "\n\nI REFUSE IF THE EDGE ASKED FOR DOES NOT EXIST in the view, and I tell you how many it "
                        + "shows. An index that is not there is the easiest mistake to make when "
                        + "dimensioning blind. And I refuse a view where nothing is left to dimension "
                        + "because everything is already dimensioned elsewhere: zero dimensions are "
                        + "not an answer of «done».",
                    Precondition = "a drawing with at least one view that shows edges",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "ONE view: its identifier in the state of the "
                                    + "drawing (V1, V2...) or its name. Empty = the first real view."),
                            "edges", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "the numbers of the edges to dimension, from the list the "
                                    + "view exposes (the first is 0). Empty = the first «count»."),
                            "count", O(
                                "type", "integer",
                                "description", "how many dimensions to put when you do not say which "
                                    + "edges. Default 4."),
                            "offset_mm", O(
                                "type", "number",
                                "description", "how far below the extent of the view to put the first "
                                    + "dimension. Default 12."),
                            "pitch_mm", O(
                                "type", "number",
                                "description", "the distance between one dimension and the "
                                    + "next. Default 10.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "hole_table",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "The hole table on a view: a tag per hole, the position from the origin and the size.",
                    Description =
                        "Puts on the drawing the HOLE TABLE of the drilled face seen in a view: for each "
                        + "hole a tag (A1, A2...), the X and Y position from a corner vertex of the "
                        + "part, and the size («Ø8», with the depth or the word for «through» as SOLIDWORKS writes it). It is the table needed by whoever drills "
                        + "a plate with many holes, in place of one dimension per hole.\n\nThe view is "
                        + "the one in which the holes are seen as circles (usually V2, from above): "
                        + "the operation chooses the face among those that look toward the reader, the "
                        + "one with the most holes. «origin» says from which corner of that face it "
                        + "is measured (default bottom_left).\n\nThe table is READ BACK: the rows "
                        + "must be the holes plus the header, and every position written "
                        + "must correspond to a hole of the part measured on the view. Measured: "
                        + "with the origin and the face badly selected SOLIDWORKS produces an EMPTY "
                        + "table without an error.\n\nI REFUSE IF no face with holes looks toward the "
                        + "reader in that view, and if the table is born empty or states positions "
                        + "the part does not have: in those cases I remove it.",
                    Precondition = "a drawing with a view of the part in which the holes are seen as circles",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "the view, as the state of the drawing calls it (V1, V2...) or by name. Default the first."),
                            "origin", O(
                                "type", "string",
                                "enum", A("bottom_left", "bottom_right", "top_left", "top_right"),
                                "description", "from which corner of the drilled face the positions are measured. Default bottom_left."),
                            "x_mm", O("type", "number", "description", "where the top-left corner of the table goes on the sheet, in mm. Default 15."),
                            "y_mm", O("type", "number", "description", "same, vertically. Default 15 below the top edge of the sheet."),
                            "tag_start", O("type", "string", "description", "the letter of the tags. Default «A».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "flat_pattern_view",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "The FLAT PATTERN view of a sheet-metal part on the drawing, measured against the part.",
                    Description =
                        "Puts on the drawing the FLAT PATTERN view of a sheet-metal part: the sheet laid "
                        + "flat, with the bend lines, as it is before going to the press. It is the "
                        + "view needed by whoever cuts the sheet.\n\nThe view is MEASURED. The "
                        + "overall size is read from the visible edges (not from the outline of the view, "
                        + "which includes the bend note and a margin — measured: 92.6 mm for a flat pattern "
                        + "of 80.7) and is compared with the flat pattern of the part measured before "
                        + "placing the view. The answer carries the overall size, the bend lines and the "
                        + "length of the flat pattern. The view goes in a free spot of the sheet (measured "
                        + "at scale 2:1 at 300/90 mm it fell in the title block).\n\nI REFUSE IF the part is not sheet metal, if "
                        + "the view is born but is not a flat pattern, or if it measures something "
                        + "other than the flat pattern of the part: in those cases I remove it, and say so only after "
                        + "reading the views of the drawing again; if it is still there, I ask you to "
                        + "remove it. I also refuse, before placing any view, when the flat pattern of the "
                        + "part does not unsuppress (or suppress again) while I measure its flat length: "
                        + "the part may then be left flattened, and the message says so.",
                    Precondition = "a drawing and, open, a sheet-metal part saved to disk",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "model", O("type", "string", "description", "the path of the sheet-metal part. Empty = the one of the drawing."),
                            "configuration", O("type", "string", "description", "the configuration of the part. Empty = the active one."),
                            "x_mm", O("type", "number", "description", "where the centre of the view goes on the sheet, in mm. Empty = a free spot outside the views and the title block."),
                            "y_mm", O("type", "number", "description", "same, vertically."),
                            "hide_bends", O("type", "boolean", "description", "without the bend lines. Default false: whoever bends wants them.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_weld_symbol",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "The ISO weld symbol on the joint between two bodies, in a view.",
                    Description =
                        "Puts on the drawing the WELD SYMBOL (ISO 2553) on the joint between two bodies "
                        + "of a weldment: the arrow on the joint line, the bead symbol (fillet, V, "
                        + "half V, butt), the throat «a» on the left and, if you give it, the "
                        + "length on the right.\n\nI FIND THE JOINT MYSELF: among the edges the view shows, "
                        + "the one where one body touches the other (the longest). The symbol is "
                        + "moved out of the part, on the side of «side», beyond the dimensions "
                        + "already placed.\n\nIT IS READ BACK: symbol, throat, length, «all around». "
                        + "The count of its texts is checked too: a code the library does not draw leaves "
                        + "one text (the throat) instead of two, and that symbol is refused. "
                        + "The code of the symbol is the one of the SOLIDWORKS library («<WELD-FILL>» "
                        + "for the fillet): measured, with a wrong code the symbol is born all "
                        + "the same, reads back the same, and does not draw the triangle.\n\nI REFUSE "
                        + "IF the part has only one body, if no joint is seen in the view, and if "
                        + "the symbol born is not the one asked for (in that case I remove it, and say "
                        + "so only after counting the weld symbols of the drawing again).",
                    Precondition = "a drawing with a view of a weldment in which the joint between two bodies is seen",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view, as the state of the drawing calls it (V1, V2...) or by name. Default the first."),
                            "variant", O("type", "string", "enum", A("fillet", "v", "half_v", "butt"), "description", "the bead. Default fillet."),
                            "throat_mm", O("type", "number", "description", "the throat of the bead in mm (ISO 2553, «a»). From 0 to 50."),
                            "length_mm", O("type", "number", "description", "the length of the bead, to the right of the symbol. Empty = the whole joint."),
                            "all_around", O("type", "boolean", "description", "the all-around circle of the bead. Default false."),
                            "field_weld", O("type", "boolean", "description", "the flag of the weld made in the field (on site). Default false."),
                            "both_sides", O("type", "boolean", "description", "the bead on both sides of the joint. Default false."),
                            "side", O("type", "string", "enum", A("top", "bottom", "left", "right"), "description", "on which side of the view the symbol goes. Default top.")),
                        "required", A("throat_mm")),
                },
                new OperationSpec
                {
                    Name = "drawing_bom",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "The BOM of an assembly on the drawing, compared with the components of the assembly.",
                    Description =
                        "Puts on the drawing the BOM of the assembly behind a view: item number, component, "
                        + "quantity. By default the first level; «parts_only» or «indented» for "
                        + "the others.\n\nI COMPARE IT: the assembly knows by itself which components it "
                        + "has and how many; the items and the sum of the quantities must match. If they do not "
                        + "match I remove it and tell you.\n\nThen I measure it and put it in a free spot of "
                        + "the sheet, outside the views and the title block.\n\nI REFUSE IF there is no assembly "
                        + "behind the view (for a weldment there is «drawing_cut_list») or if there is "
                        + "no room on the sheet."
                        + "\n\nThe count on the assembly side follows how each sub-assembly shows its "
                        + "children in the BOM (ChildComponentDisplayInBOM): in a top-level table a "
                        + "sub-assembly that promotes its children is replaced by them; in a parts-only "
                        + "table a sub-assembly that hides its children is one item. The indented table "
                        + "is compared with the rows of the assembly tree, as SOLIDWORKS writes them "
                        + "(measured): siblings with the same file and configuration make one row with their "
                        + "count; under a sub-assembly row stand the children of ONE instance, one level "
                        + "deeper (a sub-assembly used 3 times with 2 B inside: its row says 3, the B row "
                        + "under it says 2); a sub-assembly that hides its children is one row with nothing "
                        + "under it, and one that promotes them has no row and its children stand at its "
                        + "level. «counted_as» in the answer says how the expected items were counted."
                        + "\n\nA LIGHTWEIGHT SUB-ASSEMBLY REFUSES: how it shows its children in a BOM is in "
                        + "its document, which is not loaded; resolve it and ask again. A component is "
                        + "suppressed by its load state (GetSuppression2: IsSuppressed answers true on a "
                        + "lightweight component, measured), and a sub-assembly is recognised by its file "
                        + "extension, so a lightweight one is not taken for a part. A component whose load "
                        + "state cannot be read refuses the step."
                        + "\n\nIN WHICH CONFIGURATION: the table is made for the configuration the view "
                        + "shows, and the assembly is read in that same one. When the assembly has another "
                        + "configuration active, I activate the view's for the reading and put the active "
                        + "one back, reading it again: the answer gives the view's configuration and "
                        + "«assembly_active_configuration», the active one after the reading. If the view's "
                        + "configuration cannot be activated, or the active one cannot be put back, I refuse "
                        + "before inserting the table and say where the assembly stayed. The exclusion from "
                        + "the BOM is read in the configuration of the assembly that holds each component.",
                    Precondition = "a drawing with a view of a saved assembly",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view of the assembly (V1, V2...) or by name. Default the first."),
                            "variant", O("type", "string", "enum", A("top_level", "parts_only", "indented"), "description", "which BOM. Default top_level."),
                            "template", O("type", "string", "description", "the table template. Empty = the first one in the folder of the BOM templates.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "balloons",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "Ballooning: the balloons with the BOM item numbers on the components of a view, counted.",
                    Description =
                        "Does the BALLOONING: puts the balloons on the components of the assembly in a view, with the BOM item "
                        + "number (AutoBalloon5). It counts them on the view before and after, and tells you the "
                        + "numbers written. Then it compares the items of the BOM with the balloons of the WHOLE "
                        + "drawing and tells you which items are left without one (that component is not seen in "
                        + "this view: balloon another view). If every item already has its balloon elsewhere, "
                        + "it adds none and answers «nothing to add in this view».\n\nI REFUSE IF there is no BOM on the drawing yet "
                        + "(ballooning carries its item number: put it first with «drawing_bom») or "
                        + "if there is no assembly behind the view."
                        + "\n\nIf the view cannot be selected, the step is refused before AutoBalloon5 "
                        + "runs. balloons_returned is the number of notes AutoBalloon5 returned (null "
                        + "when it returned no array), and balloons_count_note appears when it disagrees "
                        + "with the count in the view.",
                    Precondition = "a drawing with the BOM and a view of the assembly",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view (V1, V2...) or by name. Default the first."),
                            "layout", O("type", "string", "enum", A("square", "circle", "above", "below"), "description", "how to arrange the balloons around the view. Default square.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "surface_finish",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "The surface roughness symbol (Ra in micrometres) on an outline edge of a view.",
                    Description =
                        "Puts the ROUGHNESS symbol (UNI EN ISO 1302) with its leader line on the outline edge of "
                        + "a view, from the «side» asked for, and moves it outside the part. «process»: material "
                        + "removal (the bar, default), any, no_removal (the little circle)."
                        + "\n\nThe Ra is READ BACK from the symbol. Measured: without moving it, SOLIDWORKS puts the symbol outside "
                        + "the frame, ignoring the coordinates; for this reason the position is set and read back."
                        + "\n\nI REFUSE IF Ra is not between 0 and 100 micrometres, if the view has no straight edge on "
                        + "that side, and if the symbol born says a different Ra.",
                    Precondition = "a drawing with a view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view, V1, V2... or by name. Default the first."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "on which side of the view the edge it attaches to is. Default «top»."),
                            "ra_um", O("type", "number", "description", "the Ra roughness in micrometres, for example 3.2"),
                            "process", O(
                                "type", "string",
                                "enum", A("material_removal", "any", "no_removal"),
                                "description", "how the surface is obtained. Default «material_removal».")),
                        "required", A("ra_um")),
                },
                new OperationSpec
                {
                    Name = "datum",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "A datum (A, B...) on an outline edge of a view.",
                    Description =
                        "Puts a DATUM (UNI EN ISO 5459): the triangle on the outline edge of the view from the "
                        + "«side» asked for, and the flag with the «letter» outside the part, on the same side. "
                        + "It is the reference that geometric tolerances cite.\n\nThe letter is READ BACK. "
                        + "Measured: left where it is born, SOLIDWORKS puts the flag on the other side of the edge "
                        + "and the line crosses the part; for this reason it is moved.\n\nI REFUSE IF the "
                        + "letter is already on the drawing — two datums with the same letter give no error and make "
                        + "every tolerance ambiguous — and if the view has no straight edge on that side.",
                    Precondition = "a drawing with a view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view, V1, V2... or by name. Default the first."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "on which side of the view the edge it attaches to is. Default «bottom»."),
                            "letter", O("type", "string", "description", "the letter of the datum, from A to Z. Default «A».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "geometric_tolerance",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "A geometric tolerance frame (flatness, perpendicularity, position...) on an edge.",
                    Description =
                        "Puts a GEOMETRIC TOLERANCE FRAME (UNI EN ISO 1101) with the arrow on the outline edge "
                        + "of the view from the «side» asked for: the «characteristic», the width of the zone in mm "
                        + "and the «references» (up to three datum letters, in the order primary, secondary, "
                        + "tertiary). «diameter» puts the Ø before the value (a cylindrical zone, for "
                        + "the position of an axis).\n\nSymbol, value and references are READ BACK from the "
                        + "frame, and so does the Ø (diameter_read, from IGtol.GetFrameDiameterSymbols)."
                        + "\n\nI REFUSE IF a form tolerance (straightness, flatness, circularity, "
                        + "cylindricity) cites a datum, if an orientation or position one cites none, if a cited "
                        + "datum is not on the drawing (put it first with «datum»), and if the frame "
                        + "born does not say what was asked for, Ø included: such a frame, or one "
                        + "that cannot be filled, is removed from the sheet, and said removed only after "
                        + "the frames of the drawing are counted again.",
                    Precondition = "a drawing with a view, and the datums to be cited already placed",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "the view, V1, V2... or by name. Default the first."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "on which side of the view the edge it attaches to is. Default «right»."),
                            "characteristic", O(
                                "type", "string",
                                "enum", A("straightness", "flatness", "circularity", "cylindricity", "profile_of_line",
                                          "profile_of_surface", "angularity", "perpendicularity", "parallelism",
                                          "position", "concentricity", "symmetry", "circular_runout",
                                          "total_runout"),
                                "description", "the geometric characteristic"),
                            "value_mm", O("type", "number", "description", "the width of the tolerance zone, in mm"),
                            "references", O(
                                "type", "array", "items", O("type", "string"),
                                "description", "the datum letters, up to three, in the order primary, secondary, tertiary"),
                            "diameter", O("type", "boolean", "description", "cylindrical zone: the Ø before the value")),
                        "required", A("characteristic", "value_mm")),
                },
                new OperationSpec
                {
                    Name = "note",
                    Family = "drawing",
                    Example = "{\"text\":\"TOLLERANZE NON INDICATE ISO 2768-mK\",\"x_mm\":20,\"y_mm\":250}",   // from `note_ends_up_on_the_sheet`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the note IS BORN, that it ends up on the SHEET and not inside
                    // a view, and that the text read back is the one asked for.
                    //
                    // It is not proven — and cannot be — on whether
                    // what is written is right: a note is free text,
                    // and «material S355» on an S275 part passes every
                    // count and every re-reading.
                    Tested = true,
                    Summary =
                        "Writes a text note on the sheet of the drawing, and READS "
                        + "BACK what is written.",
                    Description =
                        "Puts a text note on the sheet of a drawing — the general "
                        + "notes: «break all sharp edges», «galvanise after welding», "
                        + "«general tolerances ISO 2768-mK».\n\n"
                        + "I TELL YOU IF IT WAS BORN, not that I called the API: the notes are counted before "
                        + "and after. And I count them for you in TWO separate places "
                        + "— on the sheet and inside the views — because they are two "
                        + "different things and adding them would make an empty drawing look full.\n\n"
                        + "THE TEXT COMES BACK READ by SOLIDWORKS, not "
                        + "copied from what you asked: a note that appears with a different "
                        + "text reads as a right note until someone compares it.\n\n"
                        + "WHERE IT GOES: SHEET coordinates, in millimetres, by default at the "
                        + "top left. If you ask for a point outside the sheet I refuse "
                        + "— outside the sheet the note really exists and is counted, but "
                        + "when printing it does not appear, and no error would say so.\n\n"
                        + "WHAT I CANNOT KNOW is whether what you write is TRUE. "
                        + "I guarantee that the note is there and says what you wrote.",
                    Precondition = "an open drawing, with an active sheet",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "text", O(
                                "type", "string",
                                "description", "the text of the note. Required: an empty note "
                                    + "takes up a place on the sheet and says nothing."),
                            "x_mm", O(
                                "type", "number",
                                "description", "distance from the left edge of the sheet, "
                                    + "in millimetres. Default 20."),
                            "y_mm", O(
                                "type", "number",
                                "description", "height from the bottom edge of the sheet, "
                                    + "in millimetres. Default 20 mm below the top edge.")),
                        "required", A("text")),
                },
                new OperationSpec
                {
                    Name = "section_view",
                    // Z-C13 (26/9/2026): a letter already used by a section or a
                    // detail is refused; the effective letter is read back.
                    Family = "drawing",
                    Example = "{\"label\":\"A\",\"offset_mm\":0}",   // from `section_cuts_the_part`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the section view IS BORN (the count of the views
                    // goes up), that it is of the right type, and that it has
                    // HATCHING — which is the only thing that tells a
                    // section inside the material from a cutting line
                    // fallen outside, which produces a valid and empty view.
                    //
                    // It is not proven — and cannot be — on whether
                    // the cut passes where it was needed: ten millimetres further
                    // on is just as valid and does not show the hole.
                    Tested = true,
                    Summary =
                        "Creates a section view on a drawing, and checks "
                        + "that the cut met material.",
                    Description =
                        "Cuts a view of the drawing and puts the section beside it. "
                        + "It is the first thing that is missing for a real shop drawing: "
                        + "a part with an internal cavity, without a section, cannot "
                        + "be read — the hidden lines say there is a void, not what "
                        + "shape it has.\n\n"
                        + "I DRAW THE CUTTING LINE MYSELF, from side to side of the "
                        + "view: a line that only grazes it gives an empty section "
                        + "without saying anything. By default I cut in the middle; with «offset_mm» "
                        + "you move the cut, in **sheet** millimetres — not of "
                        + "the model, because on the sheet the view is scaled.\n\n"
                        + "I TELL YOU IF IT WAS BORN by counting the views "
                        + "before and after, not because the API answered with an object: "
                        + "it can answer without having done anything. The view I then read (hatch, letter) is the "
                        + "one the call returned; the last view of the sheet only when it "
                        + "returned none, and view_read_from says which. When the section "
                        + "is not born, or the cutting line cannot be selected, the line I "
                        + "drew is removed from the sheet, said removed only after the "
                        + "sheet sketch is counted again.\n\n"
                        + "I REFUSE IF THERE IS NO HATCHING. A "
                        + "section without hatching is a cutting line that fell "
                        + "outside the material: on the sheet there remains a view that "
                        + "looks good and shows nothing, and no error would say so. "
                        + "I prefer to fail and tell you where I cut.\n\n"
                        + "THE LETTER IS ONE PER DRAWING: if «label» already "
                        + "belongs to another section or to a detail I refuse and tell you "
                        + "the ones used — two «A-A» look like the same cut. The letter the "
                        + "view really has I read back to you («label_reread»).\n\n"
                        + "WHAT I CANNOT KNOW is whether that is the section that was "
                        + "needed. I can tell you that the cut met material, not that it "
                        + "passes through the cavity you wanted to show.",
                    Precondition = "an open drawing with at least one model view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "which view to section, by name. Without "
                                    + "it, the first model view of the sheet."),
                            "orientation", O(
                                "type", "string",
                                "enum", A("vertical", "horizontal"),
                                "description", "how the cutting line runs. "
                                    + "Default «vertical»."),
                            "offset_mm", O(
                                "type", "number",
                                "description", "how far to move the cut from the centre "
                                    + "of the view, in SHEET millimetres. Positive "
                                    + "to the right (or upward). Default 0, that is, "
                                    + "in the middle."),
                            "label", O(
                                "type", "string",
                                "description", "the letter of the section: «A» gives the title "
                                    + "«Section View A-A» (in the language of the installation). Default «A»."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "depth of the cut for a partial "
                                    + "section. Default 0, which cuts all the way "
                                    + "through.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "detail_view",
                    Family = "drawing",
                    Example = "{\"label\":\"B\",\"scale\":2,\"radius_mm\":8}",   // from `detail_enlarges`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the detail view IS BORN, that it is of the right
                    // type, and that the circle BECAME a
                    // detail circle on the starting view —
                    // which is what tells a true detail from a
                    // circle drawn by hand next to one more view.
                    //
                    // The hatching is NOT looked at: a detail cuts
                    // nothing, and the hatch there is zero (measured). Copying
                    // the check of the section would give an operation that
                    // always refuses.
                    Tested = true,
                    Summary =
                        "Creates an enlarged detail view on a drawing, "
                        + "and checks that the circle became a detail.",
                    Description =
                        "Enlarges a piece of a view. It is the other half of what "
                        + "a shop drawing lacks: the section shows what is inside, the "
                        + "detail shows what is too small to read at the scale of "
                        + "the drawing — a groove, a chamfer, a fillet with its "
                        + "dimension.\n\n"
                        + "I DRAW THE CIRCLE MYSELF, inside the view. By default at the centre with a radius of a "
                        + "quarter of the shorter side; with «x_mm» and «y_mm» you move it "
                        + "and with «radius_mm» you change it, in **sheet** millimetres "
                        + "— not of the model, because on the sheet the view is scaled.\n\n"
                        + "I REFUSE IF THE CIRCLE GOES BEYOND THE VIEW: a circle "
                        + "outside enlarges empty space, and the enlarged empty space is "
                        + "still a view that looks good.\n\n"
                        + "AND I TELL YOU IF THE CIRCLE BECAME A DETAIL, counting "
                        + "the detail circles on the source view before and after. "
                        + "If it does not go up I refuse, and remove both the new view "
                        + "and the circle, each said removed only after a reread: left there, "
                        + "they would be a hand-drawn circle next to a view tied to nothing, "
                        + "and no error would say so. The view I read is the one the call "
                        + "returned (view_read_from); when the detail is not born, the "
                        + "circle I drew is removed.\n\n"
                        + "THE LETTER IS ONE PER DRAWING: if «label» already belongs "
                        + "to a section or to another detail I refuse and tell you the "
                        + "ones used. The real letter I read back to you («label_reread»).\n\n"
                        + "WHAT I CANNOT KNOW is whether that is the detail "
                        + "that was needed: a circle moved by ten millimetres enlarges "
                        + "a piece of smooth plane, and is just as valid.",
                    Precondition = "an open drawing with at least one model view",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "which view to detail, by name. Without "
                                    + "it, the first model view of the sheet."),
                            "x_mm", O(
                                "type", "number",
                                "description", "how far to move the centre of the circle "
                                    + "from the centre of the view, in SHEET millimetres. "
                                    + "Positive to the right. Default 0."),
                            "y_mm", O(
                                "type", "number",
                                "description", "how far to move it vertically, "
                                    + "in SHEET millimetres. Positive upward. "
                                    + "Default 0."),
                            "radius_mm", O(
                                "type", "number",
                                "description", "the radius of the circle, in sheet "
                                    + "millimetres. Default: a quarter of the shorter side "
                                    + "of the view."),
                            "scale", O(
                                "type", "number",
                                "description", "how many times to enlarge. Default 2, that "
                                    + "is, 2:1."),
                            "label", O(
                                "type", "string",
                                "description", "the letter of the detail. Default «B», "
                                    + "because «A» is usually taken by the section.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "title_block",
                    Family = "checks",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Says which properties the title block ASKS FOR and which it SHOWS. "
                        + "Touches nothing.",
                    Description =
                        "Reads the TITLE BLOCK of the active sheet and tells you "
                        + "two different things: **which custom properties it asks for** and "
                        + "**which ones it is showing**.\n\nWHY THEY ARE TWO THINGS. A "
                        + "title block does not contain text: it contains "
                        + "links, written `$PRP:\"Descrizione\"`. Filling it means writing "
                        + "those properties with «set_property».\n\n**THE NAME MUST "
                        + "BE COPIED TO THE LETTER.** If you write it with a capital letter "
                        + "or a space different from the ones the title block references, "
                        + "**nothing visible happens**: SOLIDWORKS writes the property, "
                        + "the title block does not show it, and the PDF comes out with "
                        + "an empty box. No error, anywhere. That is why I give you "
                        + "the exact names instead of leaving you to guess them.\n\nIf a "
                        + "property is asked for and not shown I tell you: either it is not "
                        + "written yet, or it is written with another spelling."
                        + "\n\nBeware of `$PRPSHEET`: it takes the property from the "
                        + "PART in view, not from the drawing. They are two different drawers, "
                        + "and mixing them up means writing on the wrong document.\n\nIf "
                        + "the title block has no linked note I tell you, instead of "
                        + "letting you fill in properties that would not appear: that "
                        + "text is drawn inside the sheet format, not fed.",
                    Precondition = "an open drawing",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "interference_check",
                    Family = "checks",
                    Example = "{\"touching_counts\":true}",   // from `touching_is_not_interpenetrating`
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Says whether the components of the assembly interpenetrate, and by how "
                        + "much volume. Touches nothing.",
                    Description =
                        "Looks for the INTERFERENCES between the components of the open "
                        + "assembly: the places where two parts occupy the same space.\n\n"
                        + "WHY IT MATTERS. Two interpenetrating parts "
                        + "open, rebuild, export and print: **there is no error anywhere**, "
                        + "and in the workshop they do not fit. It is the defect "
                        + "you find out about when the part is made.\n\nI TELL YOU THE "
                        + "VOLUME, not only how many there are. Two interferences can be two "
                        + "tenths of a cubic millimeter — two faces grazing each other — or "
                        + "thousands, that is half a lid inside the base. **The count alone "
                        + "does not tell a problem from a detail.**\n\nFACES THAT TOUCH, "
                        + "by default, do NOT count as an interference: otherwise every "
                        + "well assembled assembly would come out broken, because "
                        + "a lid resting on the base shares a face with it. But a shaft "
                        + "in a hole of the same diameter shares the cylindrical surface, "
                        + "and there is exactly the problem: that part does not go in. If "
                        + "that is what interests you, ask me with «touching_counts», and I "
                        + "always tell you which of the two I used."
                        + "\n\nTHE COUNT AND THE LIST MUST AGREE. «how_many» is the count "
                        + "SOLIDWORKS gives; «interferences_read» is how many interferences its "
                        + "list actually held. When the two differ, «volume_total_mm3» is left "
                        + "out (a sum over part of the list would be smaller than the truth) and "
                        + "«what_is_not_known» says what was missing; a count of zero with "
                        + "interferences in the list is refused instead of answered «none»."
                        + "\n\nPOSSIBLE interferences (IsPossibleInterference) are counted apart: "
                        + "certain_interferences and possible_interferences, and the verdict counts "
                        + "only the certain ones in its alarm (both null when a flag could not be read). "
                        + "The count of components looked at excludes suppressed components "
                        + "(suppressed_components). The two settings echoed in the answer (touching "
                        + "faces, and interferences inside multibody parts) are the values SOLIDWORKS "
                        + "reads back; if one reads back different from the request, the check is refused."
                        + "\n\n"
                        + "It changes nothing: "
                        + "it can also be asked for through «measure».",
                    Precondition = "an open assembly with at least two components",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "touching_counts", O(
                                "type", "boolean",
                                "description", "true: two faces that touch exactly count as an "
                                    + "interference. It is for «does this shaft go into this hole?». "
                                    + "False (default): a lid resting on the base is not an error."),
                            "inside_parts", O(
                                "type", "boolean",
                                "description", "true: also looks between the bodies INSIDE the "
                                    + "same multibody component. False (default): only between different "
                                    + "components.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_cut_list",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    // ── PROVEN SINCE 26/9/2026 (library 0.125.0) ──────────
                    //
                    // The table is born when the part is a WELDMENT with the
                    // automatic cut list — which `cut_list` now does. The
                    // text below is from 17/9: the cause was not the drawing,
                    // it was the part (`IsWeldment` false). Measured: frame
                    // 1000 x 800, table with 2 entries, 2 x 1000 and 2 x 800.
                    //
                    // ── (17/9) NOT PROVEN: the table is not born ───────────
                    //
                    // `IView.InsertWeldmentTable` returns `null`, and it is not a
                    // problem of this operation: **on this installation
                    // NO table is born in the drawing.** Measured on
                    // 17/9/2026 — even `IDrawingDoc.InsertTableAnnotation2`
                    // with three rows, three columns and no template returns null.
                    //
                    // Tried and all negative: empty table template and
                    // real template (`cut list.sldwldtbt`); anchor 0 and
                    // TopLeft; `UseAnchorPoint` true and false; empty
                    // configuration and the exact one of the view; all three views;
                    // view activated and selected; **four different drawing
                    // templates, three of which standard** (iso, din, ansi);
                    // document already active and reactivated with `ActivateDoc3`.
                    //
                    // It is the same block as `center_marks` (entry W2.11):
                    // two annotation APIs that have nothing to do with each other,
                    // both mute on the same drawing. See entry GT2
                    // of the TODO for where to restart.
                    Tested = true,
                    Summary =
                        "Puts the cut list of the weldment on the drawing, and compares it with "
                        + "the one of the part.",
                    Description =
                        "Inserts into the drawing the CUT LIST of the weldment behind a "
                        + "view: the list of bars with lengths and quantities.\n\nAND "
                        + "I COMPARE IT. The part already knows, by itself, how many "
                        + "bodies it has in the list; the table on the drawing says its own. "
                        + "They are **two independent readings of the same fact**: if "
                        + "they do not match I tell you and I do not choose which one to "
                        + "believe. A drawing that declares a quantity different from the "
                        + "model sends the wrong number of bars to the shop, and "
                        + "neither side gives an error.\n\nIt usually means that the part "
                        + "changed after the last «cut_list»: redo that one and "
                        + "put the table back.\n\nI do not guess the quantity column by "
                        + "position: I ask the table template for it, first by type and then "
                        + "by property name. If I do not find it the comparison is not made, and "
                        + "the answer says so in «comparison_done» (false) instead of in a "
                        + "sentence at the bottom.\n\nTHE COLUMNS "
                        + "AND THE PLACE: I keep the ones the shop needs to cut (number, quantity, "
                        + "length, the two angles, description) and hide the others — the house "
                        + "table template has 17, and the table ran off the sheet. «columns» "
                        + "chooses others by header, or «all». Then I measure the table and put it "
                        + "in a free spot inside the frame, outside the views and the title block; "
                        + "if there is no room I remove it and say so."
                        + "\n\nWHAT THE COMPARISON COUNTS: the bodies of the cut-list folders at the top "
                        + "of the part's tree, in the part's active configuration. The folders of a "
                        + "sub-weldment are not descended into, and the view's configuration is not "
                        + "activated for the count (finding M32, open): on a part with sub-weldments, or "
                        + "with the view on another configuration, a mismatch can be the count's and not "
                        + "the table's.",
                    Precondition = "a drawing with a view of a weldment that already has the cut list",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "which view. Empty = the first of the drawing."),
                            "x_mm", O(
                                "type", "number",
                                "description", "where to put the top-left corner of the table. Empty = a free spot that I find myself."),
                            "y_mm", O(
                                "type", "number",
                                "description", "same, vertically."),
                            "columns", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "the headers of the columns to keep, or [\"all\"]. Empty = number, quantity, length, angles, description."),
                            "configuration", O(
                                "type", "string",
                                "description", "which configuration of the part: the guide asks for the "
                                    + "name of the «As Welded» configuration. Passed as given. Empty: "
                                    + "passed empty and SOLIDWORKS picks it; which one has not been "
                                    + "measured, and it is not necessarily the view's (finding M32, open)."),
                            "template", O(
                                "type", "string",
                                "description", "the table template. Empty = the one of the installation.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "new_sheet",
                    Family = "drawing",
                    Example = "{\"name\":\"Assembly\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Adds a sheet to the drawing, with the same projection angle as the first.",
                    Description =
                        "Adds a SHEET to the open drawing and activates it.\n\n"
                        + "I DO NOT ASK YOU FOR THE PROJECTION ANGLE, and it is not a simplification: "
                        + "I read it from the current sheet and pass it on unchanged. If sheet 2 "
                        + "were born in a different angle from sheet 1, the same drawing would "
                        + "have two sheets mirrored differently -- **neither of them gives an "
                        + "error**, and whoever cuts the part from sheet 2 cuts it flipped.\n\nAnd I read "
                        + "it back from the new sheet after creating it: passing it is "
                        + "not having it. If it does not match I stop.\n\n"
                        + "Size and scale too, if you do not say, are those of the current sheet: two sheets at different "
                        + "scales can be wanted, but not through distraction.\n\nThe name, if "
                        + "you do not say, is the next free one in the series SOLIDWORKS started "
                        + "(«Sheet2», «Sheet3»... with the word of the installation). A name already used I refuse: two sheets with the "
                        + "same name and you no longer know which one you are looking at.",
                    Precondition = "an open drawing",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "what to call it. Empty = the next free name in the series of the sheets («Sheet2»...)."),
                            "paper_size", O(
                                "type", "string",
                                "description", "A0, A1, A2, A3, A4, A4V. Empty = the one of the current sheet."),
                            "scale_numerator", O(
                                "type", "number",
                                "description", "the first number of the scale. Empty = the one of the current sheet."),
                            "scale_denominator", O(
                                "type", "number",
                                "description", "the second number of the scale. 1:2 is written as 1 and 2.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "activate_sheet",
                    Family = "drawing",
                    Example = "{\"name\":\"Sheet1\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Brings a sheet of the drawing to the front, by name.",
                    Description =
                        "Activates a SHEET of the drawing: from then on the views and "
                        + "the annotations go on that one.\n\nIT IS ASKED FOR BY NAME, "
                        + "not by number. An index would be the perfect occasion to work on "
                        + "the wrong sheet without any error.\n\nI do not believe the "
                        + "answer: after activating it I ask the drawing which sheet it "
                        + "has in front, and if it is not the one you asked for I stop -- "
                        + "otherwise the following steps would work elsewhere.\n\nThe names "
                        + "of the sheets are in «inspect_part», under «sheets»."
                        + "\n\nI REFUSE IF THE SHEET DOES NOT EXIST, and I list the ones that do with "
                        + "their names. On a multi-sheet drawing getting the sheet wrong gives no "
                        + "error: the views end up on the active one, and the shop receives a "
                        + "sheet with someone else's stuff on it.",
                    Precondition = "an open drawing with more than one sheet",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "the exact name of the sheet, as the state reports it. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is.")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "save_document",
                    Family = "drawing",
                    RequestedDocument = "any",
                    Tested = true,
                    Summary =
                        "Saves the open document.",
                    Description =
                        "Saves the active document to disk — part or drawing.\n\nIt also "
                        + "serves as the FIRST STEP of a drawing: a view attaches to the path "
                        + "of the part, so a part never saved cannot be put on a drawing."
                        + "\n\nThe file written is read back: path and size are in the "
                        + "measurements."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild, had "
                        + "rebuild errors, or had views needing an update. A failed save gives the "
                        + "error and warning names next to the raw codes.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to save. If you do not say, it is saved where it already was; a "
                                    + "never-saved document without a path is an error.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "model_view",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "A named view: isometric, front, top...",
                    Description =
                        "Adds to the sheet a NAMED VIEW of the part, at the point you say.\n\n"
                        + "The most useful is the ISOMETRIC one: on its own it makes whoever looks "
                        + "at the drawing understand the part, and the three standard views on "
                        + "their own often do not.\n\nx_mm and y_mm are the position ON THE SHEET, "
                        + "with the origin at the bottom left: on an A3 (420x297) the top right corner "
                        + "is around 320, 220.",
                    Precondition = "an open drawing and the part open beside it",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "enum", A(
                                    "isometric",
                                    "dimetric",
                                    "trimetric",
                                    "front",
                                    "back",
                                    "top",
                                    "bottom",
                                    "right",
                                    "left"),
                                "description", "which view; isometric if you say nothing"),
                            "x_mm", O(
                                "type", "number",
                                "description", "position on the sheet from the left, in mm"),
                            "y_mm", O(
                                "type", "number",
                                "description", "position on the sheet from the bottom, in mm"),
                            "model", O(
                                "type", "string",
                                "description", "the path of the part. If you do not say, the first open part is used.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "standard_views",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "The three standard views, in first-angle projection.",
                    Description =
                        "Puts on the sheet the THREE STANDARD VIEWS of the part, in FIRST-angle "
                        + "projection — the one used in Europe.\n\nThird-angle projection "
                        + "does not exist in this catalogue, on purpose: it is a twin "
                        + "call that never fails and produces a drawing with right "
                        + "and left swapped. No measurement notices, and the part is cut "
                        + "mirrored.\n\nThe views are counted before and after: if none is "
                        + "born the operation fails, instead of leaving you an empty drawing "
                        + "that looks successful."
                        + "\n\nCENTRE MARKS ON HOLES. After the views are made the drawing is rebuilt, "
                        + "and the answer gives centre_marks_after_rebuild: the marks SOLIDWORKS put "
                        + "(null when a view cannot be counted). A view made by this call that shows holes "
                        + "end-on and has no centre mark at all gets them from the bridge, through the same "
                        + "walk as the centre-mark check: one mark per hole, each counted on the sheet. "
                        + "centre_marks_added_by_bridge says how many; a view where SOLIDWORKS put some "
                        + "marks, and a view that was on the sheet before, are left as they are. When that happens the answer also gives "
                        + "holes_in_views_without_centre_marks and auto_centre_marks_option (the document "
                        + "option that inserts them, null when it cannot be read). A hole the bridge could "
                        + "not mark is counted in holes_left_without_centre_mark, with centre_marks_note "
                        + "saying what to do.",
                    Precondition = "an open drawing and the part open beside it",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "model", O(
                                "type", "string",
                                "description", "the path of the part to put in view. If you do not say, the first "
                                    + "open part is used. You find it in «documents_open» in the state.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "check_body_geometry",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Says whether the bodies are closed, sound solids. Does not touch the part.",
                    Description =
                        "Looks at the BODIES of the part and says whether they are closed "
                        + "solids with no defects.\n\nIt does not draw and does not modify anything: "
                        + "it is a question, not an operation. Call it before handing over a "
                        + "part that is going to be printed or exported.\n\nFor each body it answers: "
                        + "the type (solid, sheet, wire), how many faces it has, whether it "
                        + "is closed, how many gaps and how many topological defects, with the "
                        + "NAME of the defect and not the number. Then a verdict in one sentence: "
                        + "«printable» means ONE SINGLE solid body, closed, with no defects.\n\nAn "
                        + "OPEN body -- a surface left there by mistake -- is the case that "
                        + "matters and the one nobody sees: it gives no error anywhere, it looks like "
                        + "a part, and the STL that comes out of it encloses nothing."
                        + "\n\nWITH «body_uid» THE VERDICT IS ABOUT THAT BODY ONLY: "
                        + "«printable» and «verdict» speak for it, and «verdict_about» says so "
                        + "(«body C2»; «part» when you ask for the whole part). «bodies_in_part» "
                        + "always counts every body of the part: a sound body of a part of two "
                        + "is still a part whose STL is two separate shells."
                        + "\n\nA body whose defect check (Check3) or gap count (Diagnose) cannot be read "
                        + "is not called sound: its defect list is null with defects_not_read, and the "
                        + "printable verdict is false with the reason."
                        + "\n\nWARNING about what it does NOT look at: "
                        + "the wall thickness and the overhangs. A shell three tenths of a millimeter "
                        + "thick passes through here without a word.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "a single body, by identifier (for example «C2»). If you do not "
                                    + "say it, it looks at all of them. The identifiers are in the document "
                                    + "state, inside «bodies». With it the verdict speaks for that "
                                    + "body only; «bodies_in_part» still says how many bodies the part has. "
                                    + "The identifier is resolved through the document state and checked "
                                    + "against its signature (centre and box): if the part changed since "
                                    + "and it now names another body, the step is refused. Only the solid "
                                    + "bodies of the state have an identifier; to check sheet bodies too, "
                                    + "leave body_uid out.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "check_overhangs",
                    Family = "checks",
                    Example = "{\"threshold_deg\":70}",   // from `walls_at_sixty_degrees_do_not_print`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Says what does not print without supports, and by how much. Does not touch "
                        + "the part.",
                    Description =
                        "A wall that hangs in the void beyond a certain angle prints as lost "
                        + "filaments. SOLIDWORKS says nothing -- the part is sound, the volume "
                        + "is right, the geometry is closed -- and the defect shows only "
                        + "on the machine, once the print is finished.\n\nI LOOK AT "
                        + "EVERY SURFACE, the curved ones too: the underside of a "
                        + "horizontal cylinder hangs in the void in its lower part and not in its "
                        + "upper part, so one normal per face is not enough. I tessellate and "
                        + "look triangle by triangle, weighting by area.\n\nTHE ANGLE IS "
                        + "COUNTED FROM THE VERTICAL, as in slicers: a straight wall is 0, a "
                        + "horizontal ceiling is 90, and «threshold 45» means that beyond 45 the "
                        + "support is needed. It is not the angle with the plane, which "
                        + "is the complement: two numbers that look alike and say "
                        + "the opposite.\n\nWHAT RESTS ON THE PLANE I DO NOT COUNT. The "
                        + "bottom face looks straight down -- 90 degrees, the worst case "
                        + "-- but it rests on the plane of the machine and prints perfectly "
                        + "well.\n\nI answer with the worst overhang, how much area "
                        + "hangs, and the faces one by one with where they are, so you know whether "
                        + "to turn the part or chamfer.\n\nI ALSO LOOK AT HIDDEN BODIES (how "
                        + "many there are I tell you in «hidden_bodies»). A curved face that cannot "
                        + "be tessellated I cannot look at: I count it in «faces_not_analyzed», "
                        + "and if I found no overhangs elsewhere «prints_without_supports» "
                        + "is null, not true.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "threshold_deg", O(
                                "type", "number",
                                "description", "beyond how many degrees from the vertical the support "
                                    + "is needed. Default 45, which is the normal value for a filament "
                                    + "printer; 30-35 to be on the safe side, 60 if the machine cools "
                                    + "well."),
                            "print_direction", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "the direction the print grows in, three numbers. Default "
                                    + "[0, 0, 1]. Changing it is the way to ask «what if I printed it "
                                    + "turned?» without turning the part."),
                            "body_uid", O(
                                "type", "string",
                                "description", "a single body (for example «C2»). If you do not say it I look at all "
                                    + "of them.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "file_dependencies",
                    Family = "checks",
                    RequestedDocument = "any",
                    Tested = true,
                    Summary = "Which files the document depends on, and which are missing on the disk. Touches nothing.",
                    Description =
                        "An assembly (or a drawing) is a graph of files on the disk: I tell you which files it "
                        + "depends on, at every level, and for each one whether it is there. A file that "
                        + "is missing is a component that the assembly shows SUPPRESSED: before measuring or "
                        + "exporting it must be found again or replaced.\n\nIf it is missing, the disk says so, "
                        + "not the API: the parameter that promises to mark broken references does not mark them (measured)."
                        + "\n\nThe paths are where each file was last saved (GetDependencies with "
                        + "Searchflag false): with the search rules, a component that is not loaded "
                        + "could come back as another file with the same name found in another folder, "
                        + "which exists, and a missing file would not be reported (measured, L12-8).",
                    Precondition = "a saved document",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "mass_properties",
                    Family = "checks",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Volume, area, mass and center of gravity of the part. Touches nothing.",
                    Description =
                        "Answers «how much does this part weigh»: volume, surface "
                        + "area, mass, density and center of gravity.\n\nMASS WITHOUT A "
                        + "MATERIAL IS NOT ZERO, IT IS «UNKNOWN»: if the part has not been "
                        + "given a material I answer null and I tell you why. SOLIDWORKS would "
                        + "answer all the same, using the default density of the model -- "
                        + "usually 1000 kg/m3, that is water -- and a steel part that weighs "
                        + "like water is a believable number and wrong by a factor of eight. "
                        + "Use «apply_material» first.\n\nThe mass also arrives "
                        + "RECALCULATED by hand (volume times density) next to the one from "
                        + "SOLIDWORKS: if the two diverge, one of the measures is not in the "
                        + "units it declares, and it is the way a factor of a thousand shows "
                        + "itself instead of hiding."
                        + "\n\nON AN ASSEMBLY I weigh the whole assembly (suppressed components not, "
                        + "and I list them) and I check the mass against the sum of the parts read "
                        + "one by one: if they do not match I stop. If a part has no material, "
                        + "the mass is null and I tell you which."
                        + "\n\nWHICH COMPONENTS WEIGH ON AN ASSEMBLY (measured). Suppressed components do "
                        + "not weigh, and they are listed. ENVELOPES do not weigh: SOLIDWORKS leaves them out "
                        + "of the assembly mass, the sum of the parts leaves them out too, and the answer names "
                        + "them in «envelope_components». HIDDEN components weigh, as SOLIDWORKS weighs them, "
                        + "and are named in «hidden_components_weighed». LIGHTWEIGHT parts refuse the step: "
                        + "SOLIDWORKS would weigh them, but their material cannot be read while they are "
                        + "lightweight, so a part without material would weigh as water unseen, and the sum of "
                        + "the parts cannot read them. Resolve them in SOLIDWORKS, or pass "
                        + "«resolve_lightweight»: true and I resolve them first; they stay resolved, and "
                        + "«lightweight_resolved_on_request» says so. If one is still lightweight after that, I "
                        + "refuse. A component whose load state cannot be read refuses the step too, and so "
                        + "does one that is neither a part nor a sub-assembly by its file extension or its "
                        + "document."
                        + "\n\nWITH «body_uid», a body named from an earlier reading of the state is "
                        + "checked against its signature (centre and box): if the part changed since and "
                        + "the identifier now names another body, the step is refused instead of weighing "
                        + "the other one.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "a single body, by identifier (for example «C2»). If you do not say "
                                    + "it, it weighs them all together. A body named from an earlier reading of "
                                    + "the state is checked against its signature (centre and box): if the "
                                    + "part changed since and the identifier now names another body, the "
                                    + "step is refused instead of weighing the other one."),
                            "resolve_lightweight", O(
                                "type", "boolean",
                                "default", false,
                                "description", "assembly only: when some parts are lightweight, resolve them "
                                    + "first (ResolveAllLightWeightComponents) instead of refusing. Default false. "
                                    + "They stay resolved, and the answer says so in "
                                    + "«lightweight_resolved_on_request».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "body_bounding_box",
                    Family = "checks",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "The box of the part in mm, body by body and all together.",
                    Description =
                        "Answers «does it fit in the 300x260x40 box»: from where to where the "
                        + "part reaches on each of the three axes, and how big it is.\n\nThe "
                        + "points are the TRUE ones of the body, taken in six directions, "
                        + "not the approximated box: a bounding box declared larger than the "
                        + "real one makes everything around it be sized wrong.\n\nIt is "
                        + "the box ALIGNED TO THE AXES of the model, not the smallest "
                        + "possible one: a long part placed diagonally has a much larger bounding "
                        + "box than the one it would need turned the right way, and this "
                        + "measure does not say how to turn it."
                        + "\n\nON AN ASSEMBLY I measure each part in the place where it sits in "
                        + "the assembly and the box that contains them all; suppressed components "
                        + "not, and I list them."
                        + "\n\nON AN ASSEMBLY, LIGHTWEIGHT PARTS ARE MEASURED without resolving them: "
                        + "GetBodies3 reads the bodies of a lightweight part (measured), and the answer names "
                        + "them in «lightweight_components_measured». Envelopes and hidden components are in "
                        + "the box too, named in «envelope_components» and «hidden_components». A component "
                        + "whose load state cannot be read refuses the step, and so does one that is neither a "
                        + "part nor a sub-assembly by its file extension or its document.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "a single body, by identifier (for example «C2»). If you do not say "
                                    + "it, it measures all the bodies and the box that contains them. A body "
                                    + "named from an earlier reading of the state is checked against its "
                                    + "signature (centre and box) before it is measured: if the part "
                                    + "changed since and the identifier now names another body, the step "
                                    + "is refused instead of measuring the other one. The answer names the "
                                    + "body by the identifier asked.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "cosmetic_thread",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Says «there is a thread here» on a pin or a hole that already exists. "
                        + "It cuts nothing.",
                    Description =
                        "Puts a COSMETIC THREAD on the round edge of a pin or a hole: it is "
                        + "an annotation, not a cut. The volume of the part stays what it "
                        + "was, and if it changes I fail -- it would mean the thread was "
                        + "really cut.\n\nUse it when the thread is on a pin or on a hole made "
                        + "by hand: for a threaded hole made with the wizard there "
                        + "is already «hole_wizard», which takes the dimensions from the "
                        + "standard.\n\nIT ATTACHES TO THE EDGE, not to the face: the circular "
                        + "edge of the cylinder or of the hole.\n\nWATCH THE DESIGNATION: "
                        + "the text you write -- «M6x1» -- is what ends up on the drawing, "
                        + "WORD FOR WORD. This SOLIDWORKS call ignores the standard and the "
                        + "size: there is no table that checks whether «M6x1» is right "
                        + "for a Ø6 pin. If you write it wrong, it reaches the shop wrong.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "the ROUND edge to attach the thread to, taken from the "
                                    + "document state (for example «E3»)."),
                            "designation", O(
                                "type", "string",
                                "description", "the text of the thread, for example «M6x1». It ends up on the "
                                    + "drawing as written here."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "how deep the thread is, in mm. Not needed if it is through."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "true: the thread goes all the way through, and the depth is not needed."),
                            "diameter_mm", O(
                                "type", "number",
                                "description", "the diameter, if you want it checked: if it does not match the "
                                    + "edge's, I tell you instead of threading the wrong edge. If you do not give "
                                    + "it, I measure it from the edge.")),
                        "required", A("edge_uid", "designation")),
                },
                new OperationSpec
                {
                    Name = "variable_fillet",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A fillet that changes radius from one end of the edge to the other.",
                    Description =
                        "Rounds an edge with a radius that VARIES: large on one side, small "
                        + "on the other.\n\nIt is for where the part has to take a load at "
                        + "one point and has no room at the other -- a bracket that widens "
                        + "toward its mounting, the edge of a cover that thins out. For a "
                        + "fillet that is the same from end to end there is «fillet_edges»."
                        + "\n\nThe edge must be open, with two ends: on a circle -- "
                        + "the edge of a hole -- there is no «from» and «to» to vary between."
                        + "\n\nThe larger radius must fit on the edge: if it exceeds "
                        + "half of its length I say so before trying. And afterwards "
                        + "I check that it really removed material: a feature that is made "
                        + "without removing anything leaves the edge sharp, and the tree "
                        + "grown for nothing.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "the edge to fillet, taken from the document state "
                                    + "(for example «E7»)."),
                            "start_radius_mm", O(
                                "type", "number",
                                "description", "the radius at one end of the edge, in mm."),
                            "end_radius_mm", O(
                                "type", "number",
                                "description", "the radius at the other end, in mm.")),
                        "required", A("edge_uid", "start_radius_mm", "end_radius_mm")),
                },
                new OperationSpec
                {
                    Name = "fillet_face",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A fillet resting on two faces instead of an edge: the "
                        + "radius ball rolls touching both.",
                    Description =
                        "Rounds the transition between TWO FACES: the surface that results "
                        + "is the one left by a ball of the requested radius rolling in "
                        + "contact with both. On the edge between two faces that touch "
                        + "it gives the same part as «fillet_edges»; the difference is "
                        + "that you do not need to name an edge, the two faces are enough."
                        + "\n\nOn an OUTSIDE edge it removes material, in an inside corner it "
                        + "adds some: the answer says how much the volume changed and in which "
                        + "direction."
                        + "\n\nTHE HAND CALCULATION IS A CHECK, NOT A SIDE NOTE. When the two faces are "
                        + "flat, at 90 degrees and touch along an edge, the expected change is edge "
                        + "length x r^2 x (1 - pi/4), read before the fillet («expected_change_abs_mm3»). "
                        + "A volume change further from it than 0.05 mm3 (or one ten-thousandth of it, "
                        + "whichever is larger) is refused: the fillet is removed, the part is checked "
                        + "back, and the answer says so. On any other pair of faces the expected change "
                        + "is null and nothing is compared.\n\nI REFUSE IF the same face is "
                        + "in both sets (for the edges of a single face there is «fillet_edges»), "
                        + "and IF the two faces are flat and PARALLEL; both "
                        + "are checked before any attempt. Of two parallel faces, if they "
                        + "face the same way, or turn their backs on each other like the front "
                        + "and the back of a block, no ball touches both. Two walls that "
                        + "face each other (a slot) I let you try only with a radius equal to half "
                        + "the distance, and I tell you.\n\nAFTERWARDS I CHECK: that the new "
                        + "feature is a fillet, that the volume has changed, that the bodies "
                        + "are the ones from before and that no feature has gone into "
                        + "error. If one of these does not hold I remove the fillet, check again "
                        + "that the part is as it was, and tell you. If it is not created -- "
                        + "usually the radius, which does not fit on the faces -- I say so "
                        + "and point you to «fillet_edges».\n\nOne face per set; faces are "
                        + "named with the identifiers of the state, not with a reference to "
                        + "a step.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_1_uid", O(
                                "type", "string",
                                "description", "the first face, taken from the document state "
                                    + "(for example «F1»)."),
                            "face_2_uid", O(
                                "type", "string",
                                "description", "the second face, different from the first (for example "
                                    + "«F3»). Two flat parallel faces are not filleted together."),
                            "radius_mm", O(
                                "type", "number",
                                "description", "the radius of the fillet, in mm. It must fit on "
                                    + "both faces.")),
                        "required", A("face_1_uid", "face_2_uid", "radius_mm")),
                },
                new OperationSpec
                {
                    Name = "combine_bodies",
                    Family = "features",
                    Example = "{\"operation\":\"intersect\"}",   // from `intersect_two_cubes`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Unites, subtracts or intersects two or more bodies of the same part.",
                    Description =
                        "The boolean between BODIES of the same part: «union» fuses them into "
                        + "one, «subtract» removes the others from the main one, «intersect» keeps "
                        + "only what they have in common.\n\nIt serves molds and jigs, and "
                        + "parts designed as several bodies and then put together. Separate "
                        + "bodies are made with «move_bodies» and its copy option.\n\n"
                        + "IN A SUBTRACTION THE ORDER CHANGES THE PART: the main body is "
                        + "the one that REMAINS, the others are the ones cut out of it. If you do "
                        + "not say which, it is the first, and I write it in the answer.\n\nWhich "
                        + "bodies: the identifiers of the state (C1, C2...) with «_uid», or the bodies "
                        + "of an earlier step of this same plan with «_ref». After a cut the "
                        + "identifiers read before are no longer valid, and I refuse an old uid instead of "
                        + "combining another body.\n\nAfter the union I count the "
                        + "bodies: if there are still two, they did not fuse -- usually they do not "
                        + "really touch -- and I fail instead of confirming a part different "
                        + "from the one you think you have.\n\nAfter a boolean the identifiers "
                        + "of bodies and faces are NO LONGER valid: read the part again."
                        + "\n\nIF YOU DO NOT SAY WHICH BOOLEAN I do «union», and it is written in "
                        + "the answer. «subtract» and «intersect» are asked for by name, "
                        + "because they change the part in opposite ways.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "operation", O(
                                "type", "string",
                                "enum", A("union", "subtract", "intersect"),
                                "default", "union",
                                "description", "which boolean to do. If you do not say, it is «union», "
                                    + "and I write it in the answer."),
                            "main_body_uid", O(
                                "type", "string",
                                "description", "the body that REMAINS (for example «C1»). If you do not say, it is "
                                    + "the first. It matters only for «subtract», but writing it never hurts."),
                            "main_body_ref", O(
                                "type", "object",
                                "description", "the body that REMAINS, if an earlier step of this same "
                                    + "plan creates it (for example after a cut, which changes "
                                    + "the identifiers). Give this OR main_body_uid, not both. "
                                    + "The step must leave ONE body only.\n\nAllowed kinds:\n- bodies: the "
                                    + "bodies touched by that step",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "tool_bodies_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "the bodies to unite, subtract or intersect with the main one. If "
                                    + "you do not say, all the others."),
                            "tool_bodies_ref", O(
                                "type", "object",
                                "description", "the bodies to unite, subtract or intersect, if an earlier "
                                    + "step of this same plan creates them. Give this OR "
                                    + "tool_bodies_uid, not both.\n\nAllowed kinds:\n- bodies: the bodies "
                                    + "touched by that step, all together",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "the number of the step that creates the geometry, counting from "
                                            + "1. It must be an EARLIER step than this one."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind"))),
                        // Z-G3: default «union», declared in the answer.
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "minimum_distance",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "How far apart two faces or two edges are, checking that the "
                        + "number is not contradicted by its own points.",
                    Description =
                        "Measures the SHORTEST distance between two faces, two edges, or a "
                        + "face and an edge. It is the way to answer «how much material is left "
                        + "between the gasket groove and the outer face»: two named things and a "
                        + "number, instead of a hand calculation on the bounding box."
                        + "\n\nIt also answers with the TWO POINTS between which it measured, and with "
                        + "their distance calculated separately: if the number and the "
                        + "points do not match, the call failed and answered all the same, and in that "
                        + "case I fail instead of giving you the number.\n\nZero means that "
                        + "the two things touch, and I tell you.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "start_uid", O(
                                "type", "string",
                                "description", "identifier of the first face or of the first edge, taken "
                                    + "from the document state (for example «F3» or «E7»)."),
                            "end_uid", O(
                                "type", "string",
                                "description", "identifier of the second.")),
                        "required", A("start_uid", "end_uid")),
                },
                new OperationSpec
                {
                    Name = "measure_between",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "The measuring tool: area, perimeter, distance, angle, "
                        + "parallelism between one or two entities.",
                    Description =
                        "Measures ONE face or edge -- area, perimeter, length, radius -- "
                        + "or TWO, and then it also says distance, differences on the three axes, "
                        + "angle, whether they are parallel or perpendicular.\n\nIt is the "
                        + "same measuring tool that is used by hand in SOLIDWORKS. For the "
                        + "minimum distance alone between two things, «minimum_distance» is "
                        + "more robust: it checks the result against the points.\n\nWhat does "
                        + "not apply does not come back: the perimeter of a face that has "
                        + "none, the radius of a plane. SOLIDWORKS would give them as -1, "
                        + "which is a number and is not one."
                        + "\n\nA distance to an arc or a circle is taken as «arc_measure» says: "
                        + "centre to centre (the default), between the nearest points or between "
                        + "the farthest points. The result says which one was used («arc_measure», "
                        + "read back from SOLIDWORKS) and «projection»: none. Measured on two holes "
                        + "D10 and D20 at 50 mm centres: centre 50, minimum 35 (the wall between "
                        + "them), maximum 65. I refuse any other value of «arc_measure».",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "uid", O(
                                "type", "string",
                                "description", "the face or edge to measure, taken from the document "
                                    + "state."),
                            "uid2", O(
                                "type", "string",
                                "description", "the second one, if you want to measure BETWEEN two things."),
                            "arc_measure", O(
                                "type", "string",
                                "enum", A("centre", "minimum", "maximum"),
                                "description", "how a distance to an arc or a circle is taken: centre = centre "
                                    + "to centre (the default), minimum = between the nearest points, maximum = "
                                    + "between the farthest points. The distance between the centres is always "
                                    + "given too, centre to centre.")),
                        "required", A("uid")),
                },
                new OperationSpec
                {
                    Name = "export_stl",
                    Family = "exports",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Writes the STL for 3D printing, in millimetres. It checks the part "
                        + "first, and refuses if it is not a closed solid.",
                    Description =
                        "Writes the part as STL, the format that the slicers of 3D printers "
                        + "read.\n\nBEFORE WRITING I call «check_body_geometry» and I REFUSE "
                        + "IF the part is not a single closed solid body without defects. "
                        + "It is not caution: an STL carries nothing that says the part was "
                        + "right -- it opens, slices and prints all the same, and the hole "
                        + "shows when the part is in your hand. If you know what you are doing, "
                        + "«export_anyway»: true writes it all the same and tells you so in "
                        + "the answer.\n\nTHE UNITS ARE ALWAYS MILLIMETRES, forced by hand before "
                        + "writing and put back as they were after. The file does not carry "
                        + "them and the slicer assumes millimetres: exported in inches, the "
                        + "part comes out 25.4 times smaller and no error says so.\n\nAfter "
                        + "writing I READ THE FILE BACK: how many triangles it says it has and "
                        + "whether its size matches that number. An STL that does not match is a "
                        + "file that opens and is not the part."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild or had "
                        + "rebuild errors; preferences_not_restored names any user preference that "
                        + "could not be put back. A failed save gives the error and warning names "
                        + "next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the file, .stl extension included. If you do not "
                                    + "say, the path of the part with the extension changed is used, and then "
                                    + "the part must already have been saved."),
                            "quality", O(
                                "type", "string",
                                "enum", A("fine", "coarse"),
                                "description", "«fine» (default) makes more triangles and follows the curves "
                                    + "better; «coarse» makes a smaller file and facets the round parts."),
                            "binary", O(
                                "type", "boolean",
                                "description", "true (default): binary file, much smaller. False: "
                                    + "ASCII, readable but ten times bigger."),
                            "export_anyway", O(
                                "type", "boolean",
                                "description", "writes the file even if the check says no. Default: "
                                    + "false. Use it only knowing what was wrong.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "export_step",
                    Family = "exports",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Writes the STEP for whoever has to machine the part, and reads back from "
                        + "the file which protocol actually came out.",
                    Description =
                        "Writes the part as STEP: it is the format in which a part is sent to "
                        + "a supplier, to a customer, or taken to another CAD.\n\nUnlike the "
                        + "STL it does NOT REFUSE if the part has several bodies or surfaces: "
                        + "a STEP carries them, and a weldment is sent to the supplier "
                        + "exactly like that. The check runs all the same and its "
                        + "verdict comes back in the answer, so you know what the part you sent "
                        + "was like.\n\nAFTER WRITING I READ THE FILE BACK and say which "
                        + "protocol REALLY came out, not which you asked for: the value that "
                        + "governs it is a number with no name in the API, and the only proof "
                        + "is the FILE_SCHEMA written inside the file. If it does "
                        + "not match, it fails -- some suppliers accept only AP203.\n\n"
                        + "With «reopen_and_compare» the file is reopened in SOLIDWORKS and its "
                        + "volume compared with the part: it is the only measurement that "
                        + "says the GEOMETRY survived the translation. It costs a document "
                        + "opening, so it is not the default."
                        + "\n\nALSO AN ASSEMBLY: I write it whole and count the solids in the file against "
                        + "those of the unsuppressed parts. I refuse «reopen_and_compare» on an "
                        + "assembly: reading back the STEP of an assembly opens a window and the call "
                        + "does not return. An instance changed in the assembly (a cut: user bodies in "
                        + "GetBodies3's BodiesInfo) is a shape of its own and counts all its bodies; "
                        + "the other instances count once per file and configuration."
                        + " WHICH COMPONENTS THE FILE HOLDS (measured): HIDDEN components are not written "
                        + "by SOLIDWORKS, so they are left out of the expected count and named in "
                        + "«hidden_components_not_exported». LIGHTWEIGHT components are resolved by "
                        + "SOLIDWORKS to write the file and stay resolved; the count is made after the "
                        + "export, and «lightweight_resolved_by_the_export» says how many were resolved. "
                        + "Envelopes are written and counted."
                        + "\n\nAP242 IS WRITTEN BY SOLIDWORKS MBD, not by a protocol number: in "
                        + "SOLIDWORKS 2021 the protocol preference takes only 203 and 214, and 242 "
                        + "written there gives an AP203 file (measured). I write AP242 through "
                        + "PublishSTEP242File, with the part brought to the front. Without the MBD "
                        + "licence (the bench machine has none) I refuse, saying so; AP214 and AP203 "
                        + "are still available. With AP242 the path must end in .stp. When the file "
                        + "is written, its FILE_SCHEMA is read back as for the other two."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none»; null for AP242, whose call has no warnings mask) and "
                        + "save_warnings_note when the model needed a rebuild or had rebuild errors; "
                        + "preferences_not_restored names any user preference that could not be put "
                        + "back. A failed save gives the error and warning names next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the file, extension included: .step or .stp for AP214 "
                                    + "and AP203, only .stp for AP242 (a .step path is refused before writing). "
                                    + "If you leave it out, the part's own path is used with the extension changed."),
                            "protocol", O(
                                "type", "string",
                                "enum", A("AP214", "AP203", "AP242"),
                                "description", "AP214 (the default) also carries the colours; AP203 is the oldest and "
                                    + "the most widely read. AP242 is not a protocol number: it is written only by "
                                    + "SOLIDWORKS MBD (PublishSTEP242File), needs the SOLIDWORKS MBD licence and a "
                                    + "path ending in .stp. Without the licence I refuse and say so, and a file "
                                    + "already at the path stays as it was."),
                            "reopen_and_compare", O(
                                "type", "boolean",
                                "description", "reopens the file just written and compares the volume with the "
                                    + "part, failing beyond a thousandth of difference. Default: false, "
                                    + "because it costs a document opening. "
                                    + "The reopened file is closed by "
                                    + "reference, never by its title (with the default path its title is the part's own "
                                    + "stem); if it cannot be closed safely it stays open and the answer "
                                    + "says so. The document that was in front is brought back by its path.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "export_iges",
                    Family = "exports",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Writes the IGES, for the CADs and machines that do not read STEP.",
                    Description =
                        "Writes the part as IGES. It is for when on the other side there is a "
                        + "CAD or a machine that does not read STEP: it is an older and less precise "
                        + "format, and between the two STEP is always the first choice."
                        + "\n\nLike the STEP, it does not refuse for several bodies or surfaces, "
                        + "and the check of the part comes back in the answer.\n\nAfter "
                        + "writing I count the parameter records of the file: an IGES "
                        + "with only the headers and nothing inside exists, weighs something and opens."
                        + "\n\nThree IGES options of the SOLIDWORKS profile would add curves to "
                        + "the file or turn the solid into curves: «IGES wireframe (3D curves)», "
                        + "«Export 3D Curve features» and «Export sketch entities». I turn them "
                        + "off for the export and put them back as they were. After writing I "
                        + "also read the file's directory and count its solids (type 186) and "
                        + "surfaces: «solid_entities», «surface_entities», «entities». A file "
                        + "with neither, curves only, is refused: it opens, and it is not the part. "
                        + "After turning the three options off I read them again: "
                        + "«iges_options_not_off» lists any that stayed on or could not be read "
                        + "(empty is the proof they were off), and «iges_options_warning» says "
                        + "what to do."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild or had "
                        + "rebuild errors; preferences_not_restored names any user preference that "
                        + "could not be put back. A failed save gives the error and warning names "
                        + "next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the file, .iges or .igs extension included. If you do "
                                    + "not say, the path of the part with the extension changed is used."),
                            "representation", O(
                                "type", "string",
                                "enum", A("solids", "surfaces"),
                                "description", "«solids» (default) writes the bodies as BREP, and it is what "
                                    + "whoever has to machine the part needs; «surfaces» writes trimmed "
                                    + "faces."),
                            "reopen_and_compare", O(
                                "type", "boolean",
                                "description", "reopens the file just written and compares the volume with the "
                                    + "part. Default: false. "
                                    + "The reopened file is closed by "
                                    + "reference, never by its title (with the default path its title is the part's own "
                                    + "stem); if it cannot be closed safely it stays open and the answer "
                                    + "says so. The document that was in front is brought back by its path.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "weldment_profiles",
                    Family = "weldments",
                    Example = "{\"search\":\"TUBO STRUTTURALE QUADRO\"}",   // from `profiles_are_read_first`
                    RequestedDocument = "none",
                    Tested = true,
                    Summary =
                        "Lists the profiles of the library and their sizes, written as they must "
                        + "be copied. Touches nothing.",
                    Description =
                        "Says which profiles there are -- tubes, IPE, angles, flat bars -- and "
                        + "for each one which sizes, written exactly as they must be copied."
                        + "\n\nCALL ME BEFORE «structural_member» and «rectangular_frame». They want "
                        + "«profile_path» and «configuration» TO THE LETTER, and to the "
                        + "letter really means it: upper case, spaces and commas count. A size written "
                        + "almost right GIVES NO ERROR -- SOLIDWORKS uses the active configuration "
                        + "of the profile, the part comes out with a different wall from the "
                        + "one you think, the rebuild is clean, and you find out in the workshop."
                        + "\n\nI read the files without opening them: I open no documents "
                        + "and leave nothing lying around.\n\nIf the library is large I give you "
                        + "the first forty and tell you so: with «search» and a piece of the name "
                        + "you narrow them down.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "search", O(
                                "type", "string",
                                "description", "a piece of the profile name, to narrow down: for example «IPE», «HEA», or a word of the file name such as "
                                    + "«tube», in the language the library files are named in. Without it, it lists them all."),
                            "with_measurements", O(
                                "type", "boolean",
                                "description", "true (default) to get the sizes of each profile as well. False "
                                    + "if all you need is to know which profiles exist: it costs less.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "mate",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Constrains two components to each other, taking one face from each. "
                        + "It is the difference between an assembly and a tidy heap.",
                    Description =
                        "Puts a mate between two components of the open assembly.\n\n"
                        + "FACES ARE NAMED BY DIRECTION, not by identifier: «the lower face "
                        + "of C2 on the upper face of C1». The components are the ones "
                        + "«inspect_part» lists as C1, C2, ... and those identifiers are STABLE "
                        + "(unlike the identifiers of the faces of a part, which are renumbered "
                        + "at every reading)."
                        + "\n\nTHE DIRECTIONS ARE THE ASSEMBLY'S: the faces come from the component's "
                        + "bodies in the part's own coordinates, and each normal is turned with the "
                        + "component's placement in the assembly (Transform2) before it is compared, so "
                        + "on a turned or mirrored component «up» is the face that looks up in the "
                        + "assembly. If no flat face looks that way I say so instead of taking one at "
                        + "random; if the placement of the component cannot be read, I refuse without touching anything.\n\nAmong "
                        + "several faces that look the same way I take THE "
                        + "LARGEST: on a cover or a plate that is the one you want.\n\nWHAT "
                        + "THE MATE BLOCKS IS MEASURED (Z-C14). SOLIDWORKS gives no "
                        + "remaining degrees of freedom from here, so before the mate and after it each "
                        + "non-fixed component of the pair is moved 5 mm along X, Y and Z and turned 5 degrees "
                        + "about X, Y and Z of the assembly, one move at a time with a rebuild after each: a "
                        + "direction the mates put back is blocked, one where the component stays is free. The "
                        + "answer gives, per component in the order of the two ids, blocked_directions_before and "
                        + "blocked_directions_after (TX, TY, TZ, RX, RY, RZ), degrees_removed and direction_test; a "
                        + "fixed component is not moved (fully blocked by definition), and neither is one inside a "
                        + "sub-assembly (unknown). After the test each component is set back where it started, "
                        + "and so is any other component the rebuilds carried along; a slide the rebuild leaves is "
                        + "reported in residual_drift_mm and in the answer, not hidden.\n\n"
                        + "A REDUNDANT MATE IS REFUSED: when the mate blocks nothing that was not already blocked, "
                        + "on a pair where one component is fixed or already fully blocked, I take out the mate I "
                        + "have just made (the object AddMate5 returned, never a name) and refuse, saying which "
                        + "directions were already blocked. SOLIDWORKS itself reports nothing wrong on such a mate "
                        + "(AddMate5 answers NoError, GetWhatsWrongCount stays 0): this test is the only place it "
                        + "shows. When a position cannot be set or read, a move comes back only in part (neither "
                        + "blocked nor free), the assembly does not rebuild cleanly (a "
                        + "feature or mate in error makes every rebuild answer false), or neither component is fixed "
                        + "or fully blocked, I say so and keep the mate: a mate is never called redundant from a "
                        + "reading that could not be made.\n\nTHE COST: 13 rebuilds per non-fixed component for each "
                        + "reading (two per direction and one to put it back), and two readings per mate: 26 for "
                        + "the usual pair of a fixed and a free component, 52 when both move. On a large assembly this takes a while.\n\nI "
                        + "do not rely on the answer of the call: I count the mates of the two components "
                        + "before and after, and if they did not increase I tell you instead "
                        + "of declaring it done.\n\nIf one of the two faces cannot be selected, the "
                        + "step is refused and AddMate5 is not called.",
                    Precondition = "an open assembly with at least two components",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component_a", O(
                                "type", "string",
                                "description", "the identifier of the first component, from "
                                    + "«inspect_part»: «C1», «C2», ..."),
                            "face_a", O(
                                "type", "string",
                                "description", "which of its faces, by direction: up, down, "
                                    + "right, left, front, back. Directions of the assembly: on a turned "
                                    + "component, the face that looks that way after the rotation."),
                            "component_b", O(
                                "type", "string",
                                "description", "the identifier of the second component."),
                            "face_b", O(
                                "type", "string",
                                "description", "which of its faces, by direction."),
                            "variant", O(
                                "type", "string",
                                "description", "coincident (default), parallel, perpendicular "
                                    + "or distance. "
                                    + "I refuse concentric and tangent: they want a curved face, and here faces are chosen by direction, "
                                    + "which holds only for flat ones."),
                            "distance_mm", O(
                                "type", "number",
                                "description", "only for «distance»: how far apart they are. "
                                    + "Without it I refuse -- there is no reasonable default value."),
                            "alignment", O(
                                "type", "string",
                                "description", "«aligned» (the default) or «opposite»; any other value I refuse. "
                                    + "If SOLIDWORKS refuses for alignment, try the other: two faces that "
                                    + "look at each other want «opposite».")),
                        "required", A("component_a", "face_a", "component_b", "face_b")),
                },
                new OperationSpec
                {
                    Name = "mirror_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"component\":\"C2\",\"plane\":\"right\",\"mode\":\"instance\"}",   // from `right_bracket_and_left_bracket`
                    Summary = "Mirrors a component on a plane of the assembly: the right and the left bracket.",
                    Description =
                        "Mirrors a component on a plane of the assembly (front, top, right). YOU MUST CHOOSE "
                        + "HOW, because they are three different parts:\n- «instance»: the same file, "
                        + "mirrored. Correct for a symmetrical part.\n- «opposite_linked»: a new file that FOLLOWS "
                        + "the original. Change the right one and the left one changes too.\n- "
                        + "«opposite_unlinked»: an INDEPENDENT new file. Correct the right one and the left one stays as it "
                        + "was, and nothing tells you.\n\nAfterwards I verify it: the centre of the new part "
                        + "against the mirror of the centre of the original, and for the opposites the "
                        + "feature tree of the new file (linked or not). The new file takes the name "
                        + "of the original with the «suffix» (default «_spec»), in the same folder."
                        + "\n\nI REFUSE IF you do not give me «mode» (I explain the three), if the plane is "
                        + "not one of the three of the assembly or if the component is not there (I tell you which ones there are)."
                        + "\n\nThe three planes are recognised by their geometry (the plane's normal "
                        + "through the origin), not by their place in the tree nor by their name: if no "
                        + "plane of the assembly has that normal through the origin, I refuse and say "
                        + "what I read."
                        + "\n\nFor an instance of the same file the orientation of the new instance is "
                        + "SET («orientation», default mirrored_x_mirrored_y) and then CHECKED on the "
                        + "axes of the instance (Transform2): the centre is the same in all four "
                        + "orientations and cannot tell them apart. The answer carries «orientation» "
                        + "and «new_axes» (x, y, z unit vectors). «orientation» with an opposite-hand "
                        + "mode is refused, and so is a value that is not one of the four."
                        + "\n\nA mirror born with a rebuild error (GetErrorCode2) is refused and removed. "
                        + "Whenever a refusal removes the mirror it made, it says so only after reading the "
                        + "tree again and not finding it there; otherwise it asks you to remove it.",
                    Precondition = "an open assembly with at least one component",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "the identifier of the component, from the state: «C1», «C2», ..."),
                            "orientation", O(
                                "type", "string",
                                "enum", A("mirrored_x_mirrored_y", "mirrored_and_flipped_x_mirrored_y",
                                    "mirrored_x_mirrored_and_flipped_y", "mirrored_and_flipped_x_mirrored_and_flipped_y"),
                                "description", "only for an instance of the same file: how the new instance is "
                                    + "turned, as SOLIDWORKS names it (swMirrorComponentOrientation2_e). Default "
                                    + "mirrored_x_mirrored_y, the SOLIDWORKS default. Refused with an opposite-hand "
                                    + "mode."),
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "the plane of the assembly to mirror on."),
                            "mode", O(
                                "type", "string",
                                "enum", A("instance", "opposite_linked", "opposite_unlinked"),
                                "description", "instance (same file), opposite_linked (new file that follows "
                                    + "the original), opposite_unlinked (new independent file)."),
                            "suffix", O(
                                "type", "string",
                                "description", "for the opposites: what to add to the file name. Default «_spec».")),
                        "required", A("component", "plane", "mode")),
                },
                new OperationSpec
                {
                    Name = "fix_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"component\":\"C2\",\"grounded\":true}",   // from `fixed_is_not_mated`
                    Summary = "Fixes or frees a component of the assembly, and reads it back.",
                    Description =
                        "Fixes a component (it no longer moves, whatever mates it has) or frees it "
                        + "(only its mates hold it). It is the only state of a component that "
                        + "changes how the assembly rebuilds.\n\nFIXED DOES NOT MEAN MATED: SOLIDWORKS "
                        + "fixes the first component inserted by itself, and an assembly that "
                        + "stays still only because of that is not assembled. After changing "
                        + "it I read it back, and I tell you how many mates it has: a free component "
                        + "with zero mates is resting, it moves."
                        + "\n\nI REFUSE IF you do not give me «grounded» (true or false: they are two opposite things, there "
                        + "is no default) or if the component is not there (I tell you "
                        + "which ones there are)."
                        + "\n\nTOP-LEVEL COMPONENTS ONLY: a component inside a sub-assembly "
                        + "(«top_level»: false in the state) is refused, and the refusal names the "
                        + "sub-assembly that holds it and its uid; IsFixed and FixComponent work on the "
                        + "top level only. Open the sub-assembly to fix one of its parts, or fix the "
                        + "sub-assembly itself.",
                    Precondition = "an open assembly with at least one component",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "the identifier of the component, from the state: «C1», «C2», ..."),
                            "grounded", O(
                                "type", "boolean",
                                "description", "true to fix it, false to free it.")),
                        "required", A("component", "grounded")),
                },
                new OperationSpec
                {
                    Name = "suppress_mate",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"mate\":\"M1\"}",   // from `a_wrong_mate_is_taken_out`
                    Summary = "Turns a mate of the assembly off, or back on, by its uid or name, and reads it back.",
                    Description =
                        "Turns a mate off without losing it: the components it held stay where they are, "
                        + "but the directions it blocked are free. «reactivate»: true turns it back on. It is "
                        + "the road back from a wrong mate, and it can be walked back, so it asks no "
                        + "confirmation.\n\nTHE MATE IS NAMED from «mates_list» in the state of the assembly: "
                        + "its uid («M1» is the first mate of the tree, renumbered when one is deleted) or its "
                        + "name as SOLIDWORKS writes it («Distanza1» in an Italian session, «Distance1» in an "
                        + "English one). I read it back after the change."
                        + "\n\nI REFUSE IF the mate is not there (I tell you which ones there are) or if it is "
                        + "already as you ask (nothing changes, and I say so).",
                    Precondition = "an open assembly with at least one mate",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "mate", O(
                                "type", "string",
                                "description", "the uid («M1», «M2», ...) or the name of the mate, from «mates_list»."),
                            "reactivate", O(
                                "type", "boolean",
                                "description", "true to turn a suppressed mate back on. Default false: suppress it.")),
                        "required", A("mate")),
                },
                new OperationSpec
                {
                    Name = "delete_mate",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"mate\":\"M1\"}",   // from `a_wrong_mate_is_taken_out`
                    Summary = "Deletes a mate of the assembly by its uid or name, and reads back that it is gone.",
                    Description =
                        "Deletes a mate. It cannot be undone with another operation, only with «undo»: the "
                        + "bridge asks for a confirmation before running a plan that contains it. To turn a "
                        + "mate off and keep it, use «suppress_mate».\n\nTHE MATE IS NAMED from «mates_list» "
                        + "in the state of the assembly, by uid («M1») or by name, as in «suppress_mate». I "
                        + "read back that the name is gone and that there is one mate fewer."
                        + "\n\nI REFUSE IF the mate is not there (I tell you which ones there are).",
                    Precondition = "an open assembly with at least one mate",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "mate", O(
                                "type", "string",
                                "description", "the uid («M1», «M2», ...) or the name of the mate, from «mates_list».")),
                        "required", A("mate")),
                },
                new OperationSpec
                {
                    Name = "replace_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Changes the file of a component that is already mounted, and tells "
                        + "you whether the constraints survived SOUND or merely survived.",
                    Description =
                        "«The cover has to be redone»: changes the file of a component that "
                        + "is already mounted.\n\nALWAYS LOOK AT WHAT I ANSWER ABOUT THE "
                        + "MATES. Measured: the constraints of the old part **stay** -- "
                        + "they are not lost -- but staying does not mean working. A constraint "
                        + "that pointed at a face of the old part survives in the tree "
                        + "and may go into ERROR, and then the count still sees it and "
                        + "everything looks fine.\n\n**A broken constraint is worse than a "
                        + "missing one**: a missing one shows, a broken one seems to hold. "
                        + "So I tell you two things, not one: how many mates there are "
                        + "before and after, AND how many problems the document has before and after. "
                        + "If the problems increased I shout it, and the thing to do is to "
                        + "remove those constraints and put them back with «mate»."
                        + "\n\nTHE NEW FILE MUST ALREADY BE OPEN in SOLIDWORKS, "
                        + "as for «insert_component»: copy the path from «documents_open».\n\n"
                        + "I REFUSE IF you give the component the file it already has: "
                        + "it would succeed and change nothing, and you would believe "
                        + "you had changed the part."
                        + "\n\nTHE CONFIGURATION IS CHECKED, NOT YET CHOSEN. A «configuration» the new "
                        + "file does not have is refused before the swap, and nothing changes. The swap "
                        + "asks SOLIDWORKS to match the configuration of the old component with one of "
                        + "the new file (its «match name» choice); the choice that uses the named "
                        + "configuration waits for a probe that shows it opens no window. So after the "
                        + "swap I read back the configuration the new component references and give it "
                        + "in the answer («configuration»), and, when you asked for one, "
                        + "«configuration_as_asked»: false means SOLIDWORKS took another one, the verdict "
                        + "opens with a WARNING, and the swap stays (undo it if the size matters)."
                        + "\n\nTOP-LEVEL COMPONENTS ONLY: a component inside a sub-assembly is refused "
                        + "before the call (ReplaceComponents2 wants a top-level component). The swap "
                        + "counts as done only when a component took up the new file that was not on it "
                        + "before AND the chosen component no longer uses its old file: another component "
                        + "already mounted on the new file does not count. «took_its_place» names the "
                        + "component(s) that took it; the C1..Cn numbers follow the names, so read the "
                        + "state again before naming one by uid.",
                    Precondition = "an open assembly, and the new file already open",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "the identifier of the component to replace, from "
                                    + "«inspect_part»: «C1», «C2», ..."),
                            "path", O(
                                "type", "string",
                                "description", "full path of the NEW file, already open in "
                                    + "SOLIDWORKS."),
                            "configuration", O(
                                "type", "string",
                                "description", "a configuration of the new file, whole name. Refused "
                                    + "before the swap if the file does not have it. SOLIDWORKS matches "
                                    + "the old component's configuration by name, so the one you get may "
                                    + "differ: the answer reads it back («configuration_as_asked»). "
                                    + "Empty: not checked; the guide says an empty name means the default "
                                    + "configuration of the new file."),
                            "all_instances", O(
                                "type", "boolean",
                                "description", "true (default): if the same part is mounted several times, "
                                    + "it replaces all of them. False: only that one.")),
                        "required", A("component", "path")),
                },
                new OperationSpec
                {
                    Name = "insert_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Puts an already open part into the open assembly, and tells you whether "
                        + "it is constrained or merely resting there.",
                    Description =
                        "Inserts a component into an assembly.\n\nWHAT YOU MUST KNOW BEFORE "
                        + "ANYTHING ELSE: inserting is NOT mating. The part appears, "
                        + "the tree fills up, the rebuild is clean -- and the component is "
                        + "resting there, free. Four free components look like an assembly, "
                        + "they open, they turn, they export, and they are put together "
                        + "wrong.\n\nSo I answer with «grounded», «mates» and «resting». "
                        + "Not fixed and zero mates means RESTING: "
                        + "the part is just sitting there, and as long as it is so the "
                        + "assembly is not assembled, it is a tidy heap.\n\nWHAT I CANNOT "
                        + "TELL YOU, and I say so: **how much** a component is still "
                        + "free. SOLIDWORKS does not give me the residual degrees of freedom "
                        + "from here -- it answers «not available» on every component, fixed "
                        + "or free -- and an invented number would be worse than no number. "
                        + "So «resting: false» means «it has at least one mate», NOT "
                        + "«it no longer moves». One coincident mate leaves three of the six "
                        + "directions free (measured), "
                        + "and «mate» says which directions each mate blocks.\n\nTHE COMPONENT MUST ALREADY BE OPEN in "
                        + "SOLIDWORKS: I look for it by path, and on a closed file the insertion "
                        + "answers «not done» without saying why. The paths you can use are "
                        + "the ones in «documents_open» of the state: copy them from "
                        + "there."
                        + "\n\n«configuration» chooses the configuration of the part to mount. The name "
                        + "is checked against the configurations of the open file BEFORE inserting, "
                        + "compared whole (the part in angle brackets included): a name the file does "
                        + "not have is refused and nothing is inserted. Then SOLIDWORKS is asked for it "
                        + "(UseConfigForPartReferences true) and the configuration the new component "
                        + "really references is read back: if it is another one, the component is taken "
                        + "out again and the step is refused, and the refusal says whether the removal "
                        + "was confirmed by counting the components. Without a name the component comes "
                        + "in with the file's last saved configuration "
                        + "(swAddComponentConfigOptions_CurrentSelectedConfig), and the answer's "
                        + "«configuration» says which one.\n\nWATCH THE COORDINATES, because the two numbers do not match "
                        + "and it is not an error: «x_mm, y_mm, z_mm» place the **centre "
                        + "of the bounding box**; «origin_mm», which I answer and which "
                        + "you will read back in the state, is where the part's **origin** "
                        + "ended up. If the part has its origin on a face -- that is, almost "
                        + "always -- the two points are half the extent apart. Ask for z=30 "
                        + "on a part 4 thick and the origin comes out at z=30 but y=-2: "
                        + "neither number is wrong, they are two different things.\n\nI do not "
                        + "rely on the answer of the "
                        + "insertion: I count the components before and after, and if they "
                        + "did not increase by one I tell you instead of declaring it done.",
                    Precondition = "an open assembly, and the component to insert already open",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "full path of the file to insert, copied from "
                                    + "«documents_open». It must already be open in SOLIDWORKS."),
                            "x_mm", O(
                                "type", "number",
                                "description", "where the CENTRE of the component's bounding box goes, "
                                    + "in mm. Default 0. It is not a mate: it is only "
                                    + "where to rest it. And it is NOT where its origin ends up: "
                                    + "if the part has its origin on a face, the origin moves "
                                    + "by half the extent. I answer with «origin_mm» for this."),
                            "y_mm", O(
                                "type", "number",
                                "description", "where the CENTRE of the bounding box goes, in mm. Default 0."),
                            "z_mm", O(
                                "type", "number",
                                "description", "where the CENTRE of the bounding box goes, in mm. Default 0."),
                            "configuration", O(
                                "type", "string",
                                "description", "the configuration of the part to mount, by its whole "
                                    + "name (the part in angle brackets included). A name the file does "
                                    + "not have is refused before inserting; the component comes in with "
                                    + "it, read back, or is taken out again. Empty: the file's last saved "
                                    + "configuration.")),
                        "required", A("path")),
                },
                new OperationSpec
                {
                    Name = "circular_sweep",
                    Family = "extrusions",
                    Example = "{\"path_mm\":[[0,0,0],[100,0,0]],\"diameter_mm\":20}",   // from `straight_pipe_area_per_length`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A solid tube of given diameter along a path of points. On a "
                        + "straight path I check that the volume is area times length.",
                    Description =
                        "Makes a shape that follows a path: a cable gland, a handle, a bent "
                        + "tube. It is the first operation of the catalogue that makes "
                        + "something that is not straight.\n\nTHE PROFILE IS A NUMBER, not a sketch: "
                        + "you give the diameter and that is all. The path is a list of "
                        + "points in space, and I draw it myself: I do not ask you for a sketch "
                        + "because in the document state sketches have no identifier, "
                        + "and you would have no way to point one out to me.\n\nON A "
                        + "STRAIGHT PATH THE CALCULATION IS DONE BY HAND, and I do it: the "
                        + "volume must be the area of the circle times the length, and if it is "
                        + "not I refuse. On a path with elbows it is not -- at the elbows "
                        + "the material overlaps and the real volume is smaller -- and then I "
                        + "give you the theoretical one, telling you it is an upper "
                        + "limit, not the answer.\n\nA BEND TIGHTER THAN THE RADIUS OF THE TUBE "
                        + "cannot be made: the surface would cross itself. In that case "
                        + "SOLIDWORKS creates nothing and does not say why; I tell you.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path_mm", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "the points of the path, at least two, each [x, y, z] in mm. "
                                    + "For example [[0,0,0], [100,0,0]] makes a straight tube 100 long."),
                            "diameter_mm", O(
                                "type", "number",
                                "description", "the diameter of the tube, in mm.")),
                        "required", A("path_mm", "diameter_mm")),
                },
                new OperationSpec
                {
                    Name = "loft_two_circles",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"diameter1_mm\":40,\"diameter2_mm\":20,\"distance_mm\":50}",   // from `loft_is_not_a_cone`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A transition between two circles of different diameter, and I tell you by "
                        + "how much it departs from the truncated cone.",
                    Description =
                        "Blends two different round sections: a reducer, an inlet, a transition "
                        + "from a large tube to a thin one.\n\nA LOFT BETWEEN TWO "
                        + "CIRCLES IS NOT A CONE, and it is the thing worth knowing. Its surface "
                        + "is a spline approximation of the cone: the volume departs from "
                        + "the formula of the truncated cone by a few thousandths of a "
                        + "percent, always on the low side. I give you both numbers and "
                        + "the difference, so you do not chase a defect that is not there. If "
                        + "you need the exact cone, «revolve» on a trapezoid does it."
                        + "\n\nThe two circles lie on planes parallel to the one you "
                        + "choose, at the distance you say. You can offset them from each other "
                        + "to make an oblique transition.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "the plane of the first circle. Default «front»."),
                            "diameter1_mm", O(
                                "type", "number",
                                "description", "the diameter of the first circle, on the chosen plane."),
                            "diameter2_mm", O(
                                "type", "number",
                                "description", "the diameter of the second."),
                            "distance_mm", O(
                                "type", "number",
                                "description", "how far the second circle is from the first, in mm. "
                                    + "Greater than zero: the second circle goes on the side the chosen "
                                    + "plane faces (+Z for front, +Y for top, +X for right). A "
                                    + "negative or zero distance is refused."),
                            "offset1_mm", O(
                                "type", "number",
                                "description", "how far to move the first circle sideways. Default 0."),
                            "offset2_mm", O(
                                "type", "number",
                                "description", "how far to move the second one sideways: needed for "
                                    + "an oblique transition. Default 0.")),
                        "required", A("diameter1_mm", "diameter2_mm", "distance_mm")),
                },
                new OperationSpec
                {
                    Name = "sketch_picture",
                    Family = "references",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary = "Puts a photo or a hand sketch, to scale, on a plane: to trace over.",
                    Description =
                        "Puts an image (a photo of the part, a sketch with dimensions in pen) into a sketch "
                        + "on a plane, TO SCALE: you tell me how wide the whole image really is "
                        + "(«width_mm» — a known measure divided by its pixels, times the image's width "
                        + "in pixels) and where its lower left corner goes. Then you trace over it on "
                        + "the same plane with the sketch operations.\n\nI VERIFY THE SCALE: the first and "
                        + "the last pixel of the first row, in the sketch, must be as far apart as the "
                        + "millimetres per pixel say, within 1%. I answer with the rule for going from a "
                        + "pixel to millimetres.\n\nI REFUSE IF it is a PDF (it cannot be inserted: convert "
                        + "it to PNG), a file that does not exist or is not an image, or a width that "
                        + "is not positive."
                        + "\n\nOver 2048 px SOLIDWORKS compresses the picture in a part, even with "
                        + "HighestResolution (measured: 4096 x 2048 is held as 2048 x 1024). «pixel» and "
                        + "«mm_per_pixel» are the FILE's, the pixels you count in the image: the scale is "
                        + "«width_mm» divided by the FILE's pixel width. «solidworks_pixels» is the map "
                        + "SOLIDWORKS holds, and «compression» says when the two differ. I also refuse when "
                        + "the file's pixel size cannot be read and SOLIDWORKS holds 2048 px or more on a "
                        + "side, and when the picture SOLIDWORKS holds does not have the file's shape. "
                        + "I also refuse when SetSize or SetOrigin answers false, or when the width, "
                        + "height or lower left corner read back is more than 0.01 mm from what was "
                        + "asked; the picture then stays in the sketch, to be removed.",
                    Precondition = "an open part (even an empty one)",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "the image file: PNG, JPG, BMP, GIF or TIF."),
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "the plane to put it on."),
                            "width_mm", O(
                                "type", "number",
                                "description", "how wide the WHOLE image really is, in mm: the scale."),
                            "x_mm", O("type", "number", "description", "where the lower left corner goes, x. Default 0."),
                            "y_mm", O("type", "number", "description", "where the lower left corner goes, y. Default 0.")),
                        "required", A("path", "plane", "width_mm")),
                },
                new OperationSpec
                {
                    Name = "plane_three_points",
                    Family = "references",
                    Example = "{\"point1_mm\":[0,0,0],\"point2_mm\":[50,0,0],\"point3_mm\":[100,10,0]}",   // from `limit_three_points_in_a_row`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "An oblique plane, given by three points; I read its normal back to tell you "
                        + "it is the one asked for.",
                    Description =
                        "«create_plane» can only make a plane OFFSET from one of the three standard "
                        + "ones: it serves everything that is aligned with the axes and nothing "
                        + "else. For a tilted hole, an oblique face or a bracket at 30 degrees "
                        + "there was no plane to draw on.\n\nTHE POINTS ARE COORDINATES, "
                        + "not vertices of the part: three triples of numbers in millimetres, "
                        + "in the model's coordinate system. I do not ask you to point me at "
                        + "vertices because in the document state vertices have no identifier "
                        + "-- faces and edges do, vertices do not.\n\nI CHECK THAT THE PLANE IS "
                        + "THE RIGHT ONE. From the three points I compute the normal, then I "
                        + "read back that of the plane just created and compare the two: if "
                        + "they do not coincide I refuse. «One more feature in the tree» would say "
                        + "the same of a crooked plane, and on a crooked plane you would draw "
                        + "without noticing.\n\nTHREE POINTS IN A LINE DO NOT MAKE A PLANE, and "
                        + "I tell you before trying: infinitely many planes pass through a line.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "point1_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "the first point, three numbers in mm: [x, y, z]."),
                            "point2_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "the second point."),
                            "point3_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "the third point. It must not lie on the line through the other two.")),
                        "required", A("point1_mm", "point2_mm", "point3_mm")),
                },
                new OperationSpec
                {
                    Name = "base_flange",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A sheet metal part bent from an open profile: thickness, bend radius, depth.",
                    Description =
                        "Makes a SHEET METAL part from an open profile drawn on a base plane: an L, "
                        + "a U, a Z. Every corner of the profile becomes a bend with the radius asked "
                        + "for.\n\nTHE POINTS ARE THE INNER FACES when the profile goes in the direction in "
                        + "which the thickness stays on the RIGHT: measured on an L of 50 + 30 given "
                        + "from the top towards the right, the flat pattern comes out 80.712 mm, equal "
                        + "to the hand calculation on the inner faces. Given the other way round, the "
                        + "thickness goes to the other side and the points become the OUTER faces: "
                        + "the answer always says which of the two («points_are_faces»), because "
                        + "between the two there are 2 times the thickness per bend.\n\nThe answer also "
                        + "carries the FLAT PATTERN: the length of the flattened part measured by "
                        + "SOLIDWORKS, next to the one calculated by hand with the K factor read "
                        + "back from the part. If they do not match, it says so, before the DXF "
                        + "goes to cutting.\n\nI REFUSE IF a segment is shorter than the bends that eat it "
                        + "(a 1 mm side with a radius of 2): SOLIDWORKS would give a red feature without "
                        + "saying which side. I also refuse a flange that is born in error or that "
                        + "adds less than half of the material of the hand calculation."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "a part with no bodies, or one to add a sheet metal body to",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "the base plane the profile lies on. Default «front»."),
                            "points", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "the OPEN profile, at least two points [x, y] in mm on the plane. "
                                    + "An L: [[0,30],[0,0],[50,0]]."),
                            "thickness_mm", O("type", "number", "description", "the thickness of the sheet"),
                            "radius_mm", O("type", "number", "description", "the INNER radius of the bends"),
                            "depth_mm", O("type", "number", "description", "how deep the sheet is, perpendicular to the plane")),
                        "required", A("points", "thickness_mm", "radius_mm", "depth_mm")),
                },
                new OperationSpec
                {
                    Name = "edge_flange",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A new bend on a border edge of a sheet metal part that already exists.",
                    Description =
                        "Adds a 90 degree FLANGE on a border edge of a sheet metal part (made "
                        + "with «base_flange»): this is how a box is closed, with bends on edges of "
                        + "different directions.\n\nThe edge is the one between the wide face of "
                        + "the sheet and an END face, as wide as the thickness; the flange rises on "
                        + "the side the wide face faces. «length_mm» is measured to the "
                        + "OUTER VIRTUAL SHARP, and the material stays inside the bounding box "
                        + "as before: the side you bend on gets shorter by radius + thickness. "
                        + "Thickness, radius and K factor are those of the sheet, read back.\n\n"
                        + "The answer carries the added volume next to the hand calculation "
                        + "(the extra flat pattern, (L - R - T) + BA - (R + T), times the thickness "
                        + "and the length of the edge), and the length read back from the flange."
                        + "\n\nI REFUSE IF the length leaves nothing straight (not beyond "
                        + "radius + thickness), if the edge is not straight or is not on the border "
                        + "of the sheet, or if the flange is created without adding material."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "a sheet metal part",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "the border edge to bend, from the document state (E1, E2...)"),
                            "length_mm", O(
                                "type", "number",
                                "description", "how long the flange is, measured to the outer virtual sharp")),
                        "required", A("edge_uid", "length_mm")),
                },
                new OperationSpec
                {
                    Name = "hem",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "The edge of a sheet metal part folded back on itself, closed or open.",
                    Description =
                        "Folds the edge of a sheet metal part (made with «base_flange») back on itself: "
                        + "the hem that removes the sharp edge and stiffens the border. «closed» is "
                        + "flattened against the sheet; «open» leaves «gap_mm» between the edge and the "
                        + "sheet. It stays inside the bounding box as before.\n\nThe edge is the one between the "
                        + "wide face and an end face, as wide as the thickness. The answer "
                        + "carries the added volume next to the hand calculation: for the open one it is the "
                        + "180 degree bend with the inner radius equal to half the gap, measured three times "
                        + "to the thousandth; for the closed one the calculation uses a radius of 0.05 mm, measured "
                        + "with thickness 2, and it is stated but does not decide.\n\nI REFUSE IF the hem does not "
                        + "leave anything straight, if «open» has no gap, if the edge is not "
                        + "straight or is not on the border, or if the hem is created without adding material."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "a sheet metal part",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "the border edge to fold, from the document state (E1, E2...)"),
                            "variant", O(
                                "type", "string",
                                "enum", A("closed", "open"),
                                "description", "closed (flattened) or open (with a gap). Default «closed»."),
                            "length_mm", O(
                                "type", "number",
                                "description", "how long the folded edge is"),
                            "gap_mm", O(
                                "type", "number",
                                "description", "only for «open»: the space between the folded edge and the sheet")),
                        "required", A("edge_uid", "length_mm")),
                },
                new OperationSpec
                {
                    Name = "sketched_bend",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Bends a sheet metal part along a line drawn on one of its faces.",
                    Description =
                        "Bends a sheet metal part that already exists along a LINE drawn on one "
                        + "of its planar faces, from the point «start_mm» to the point «end_mm» (points "
                        + "[x, y, z] in mm on the plane of the face). The line crosses the face from edge to edge.\n\n"
                        + "The sheet bends towards the side the face FACES: to bend "
                        + "the other way, draw the line on the other face. The radius is that of the "
                        + "sheet; which side stays fixed is chosen by SOLIDWORKS, and the answer "
                        + "carries the bounding box before and after.\n\nA bend does not add "
                        + "sheet: volume and flat pattern stay what they were, and if they change the "
                        + "answer says so.\n\nI REFUSE IF the angle is not between 0 and 180 degrees "
                        + "exclusive, if the face is not planar, if an end of the line is not "
                        + "on the face, or if the bend is created without bending anything."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened), and before bending I refuse if that happens.",
                    Precondition = "a sheet metal part",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "the planar face the line is drawn on, from the document state (F1, F2...)"),
                            "start_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "one end of the bend line, [x, y, z] in mm on the face"),
                            "end_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "the other end, [x, y, z] in mm on the face"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "the bend angle. Default 90.")),
                        "required", A("face_uid", "start_mm", "end_mm")),
                },
                new OperationSpec
                {
                    Name = "flat_pattern_dxf",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "The DXF of the flat pattern of a sheet metal part, for cutting.",
                    Description =
                        "Writes the FLATTENED sheet metal part to a DXF: the drawing that goes to "
                        + "the cutting machine.\n\nTHE PART MUST BE SAVED: on a part with no file "
                        + "SOLIDWORKS opens «Save As» and waits for someone to click — measured. I refuse "
                        + "such a part before calling SOLIDWORKS.\n\nThe DXF is READ BACK: its bounding "
                        + "box must have the flat pattern measured on the part as its long side, "
                        + "otherwise I refuse it — a DXF of something else opens just the same. "
                        + "Without «path» it goes next to the part, with its name."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "a sheet metal part, saved",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the .dxf. Default: next to the part, with its name.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "dimension_scheme",
                    Family = "dimensions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Dimensions the part in the model with DimXpert, from three reference faces.",
                    Description =
                        "Puts on the PART (not on a drawing) the dimensions that a draftsman "
                        + "would put: thicknesses, sides, hole diameters and their position from "
                        + "the datums. It is the DimXpert of SOLIDWORKS, from the core, "
                        + "with no add-ins.\n\nTHE DATUMS ARE THREE PLANAR FACES, in order: the "
                        + "primary (usually the resting one), the secondary and the tertiary (two "
                        + "perpendicular sides). The position dimensions start from them. With fewer "
                        + "than three it refuses: DimXpert would not refuse, it would put a "
                        + "single dimension.\n\nThe answer carries the VALUES of the dimensions "
                        + "(«values_mm»), read from the dimensions created: it is the way to see whether "
                        + "it dimensioned what was needed. The answer of SOLIDWORKS to the call "
                        + "(«auto_dimension_scheme_response») is False even when it did "
                        + "dimension — measured: it is not looked at.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "references_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "THREE planar faces from the document state: primary, secondary, tertiary"),
                            "tolerance", O(
                                "type", "string",
                                "enum", A("plus_minus", "geometric"),
                                "description", "plus/minus on the dimensions (default) or geometric tolerances")),
                        "required", A("references_uid")),
                },
                new OperationSpec
                {
                    Name = "draft",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Tilts faces by an angle, relative to a face that stays fixed. "
                        + "Measures the angle on the part, not on the feature.",
                    Description =
                        "The draft angle: the walls of a part made in a mold must lean by a "
                        + "degree or two, or the part does not come out of the form. It also "
                        + "serves castings and features with a draft angle."
                        + "\n\nTWO DIFFERENT FACES, AND THEY MUST NOT BE CONFUSED. The NEUTRAL face is the "
                        + "one that stays fixed: it is the plane the angle is measured from, "
                        + "and it goes in «neutral_face_uid». The faces to tilt go "
                        + "in «faces_uid». If you swap them, SOLIDWORKS makes a feature "
                        + "all the same, gives it the same name and reports no error -- but "
                        + "it has drafted other faces, by another amount. I notice and "
                        + "I stop you.\n\nHOW I NOTICE. The feature, when the angle "
                        + "is requested, always declares what you asked it for -- even when "
                        + "it has not tilted anything. So I count the faces of the part that "
                        + "really are at the right angle to the neutral one, before and after, "
                        + "and if not enough have appeared I refuse."
                        + "\n\nTHE DIRECTION. With «flip» the wall leans the other way: the part grows instead "
                        + "of shrinking. I answer how much material moved and in which "
                        + "direction, so you see it without looking at the part from the side."
                        + "\n\nFLAT FACES ONLY: a drafted cylindrical face becomes conical and "
                        + "does not have a single inclination, so I do not count it.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "neutral_face_uid", O(
                                "type", "string",
                                "description", "the face that STAYS FIXED, the angle is measured from it (for "
                                    + "example «F1»). Usually it is the bottom or the mouth of the mold."),
                            "faces_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "the faces to tilt. They must touch the neutral one."),
                            "angle_deg", O(
                                "type", "number",
                                "description", "by how much to tilt them, in degrees. For a mold 1-3 "
                                    + "degrees; for a sand casting even 5."),
                            "flip", O(
                                "type", "boolean",
                                "description", "true to make the walls lean the other way -- the part "
                                    + "grows instead of shrinking. Default false.")),
                        "required", A("neutral_face_uid", "faces_uid", "angle_deg")),
                },
                new OperationSpec
                {
                    Name = "rib",
                    Family = "features",
                    Example = "{\"plane\":\"front\",\"distance_mm\":25,\"start_mm\":[10,40,25],\"end_mm\":[40,10,25],\"thickness_mm\":6}",   // from `rib_in_the_corner_of_the_l`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "A stiffening rib: fills, in the plane of a line, the space between the "
                        + "line and the walls of the part. Measures the material added.",
                    Description =
                        "Stiffens a thin wall: the corner of an L bracket, the bottom of a hollowed box. "
                        + "You give a LINE with two ends «start_mm» and «end_mm» (points [x, y, z] in mm "
                        + "in the model system) that lies on a plane: one of the three base planes "
                        + "(«plane»), possibly moved parallel to itself by «distance_mm» (I create the offset "
                        + "plane myself). The rib grows IN the plane of the line, from the line to the "
                        + "walls that the line -- extended -- meets, and it is «thickness_mm» thick, "
                        + "split in half between the two sides of the plane.\n\n"
                        + "THE LINE LIES IN THE EMPTY SPACE, with its ends (or their extension) on the walls to be joined. "
                        + "Example: L 60x60, 10 thick and 50 deep, front plane at distance 25, line from [10, "
                        + "40, 25] to [40, 10, 25]: the rib is the triangle in the corner, 450 mm2 x "
                        + "6 = 2700 mm3.\n\n"
                        + "THE DIRECTION OF THE MATERIAL I try myself: first as given, then flipped, and I keep the first one in "
                        + "which the rib is created and the part grows. It depends on the shape (in the L it "
                        + "is the one as given, in the hollowed box the flipped one) and the answer "
                        + "says which I used and how many attempts. If the line lies between walls on both "
                        + "sides, the one as given wins.\n\n"
                        + "I REFUSE IF an end of the line is not on the plane given (I "
                        + "do not project it silently), IF the line is shorter than one millimeter, and "
                        + "IF the rib is created in neither of the two directions: usually it means there "
                        + "is nothing to meet from the line, as on a plate without walls (for a raised "
                        + "feature on a flat face there is «extrude_on_face»). When I refuse I remove the "
                        + "sketch and the plane I had made: the part stays as it was.\n\n"
                        + "I do not make drafted ribs and I do not look at WHICH walls the rib has joined: "
                        + "I measure that it was created, the material added and that the part did not "
                        + "break.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "the base plane parallel to that of the rib: front "
                                    + "(XY), top (XZ) or right (YZ)"),
                            "distance_mm", O(
                                "type", "number",
                                "description", "by how much the plane of the rib is moved from the base plane, "
                                    + "along its normal (+Z, +Y, +X). Negative for the other direction. "
                                    + "Default 0: the rib lies on the base plane."),
                            "start_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "one end of the line, [x, y, z] in mm, on the plane of the rib"),
                            "end_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "the other end, [x, y, z] in mm, on the plane of the rib"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "the thickness of the rib, in mm, split in half between the two "
                                    + "sides of the plane. For a bracket 10 thick, usually 4-6.")),
                        "required", A("plane", "start_mm", "end_mm", "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "suppress_instance",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Removes one or more copies from a pattern already made, and "
                        + "refuses if the copy was not there.",
                    Description =
                        "A grid of holes that skips one where a rib passes, a row of slots "
                        + "that stops before the bend. The pattern is made first, whole, with "
                        + "«linear_pattern» or «circular_pattern»; then what must not be "
                        + "there is removed.\n\nCOPIES ARE COUNTED FROM 1, AND THE ORIGINAL "
                        + "IS NOT A COPY. In a row of four, 1 is the first after the original "
                        + "and 3 is the last.\n\nI REFUSE IF THE COPY WAS NOT THERE, "
                        + "and that is the reason this operation checks instead of just "
                        + "writing. A pattern builds only the copies that fit in "
                        + "the material and answers «succeeded» even when it made fewer "
                        + "than were asked for: asking to remove the fourth of four, "
                        + "when the fourth fell outside the part, SUCCEEDS and changes "
                        + "nothing. I count the faces before and after, and if they did not "
                        + "drop I tell you that copy was not there and put everything back as "
                        + "it was.\n\nA removed copy can be put back with «reactivate».\n\n"
                        + "NOT ALL PATTERNS CAN DO IT: those driven by a sketch, a chain "
                        + "or a dimension do not have numbered copies. A mirror has a single "
                        + "copy: it is removed by deleting it. A FILL pattern and a TABLE-driven "
                        + "pattern do skip "
                        + "instances, but a fill pattern skips them by (row, column) and a table "
                        + "pattern by the rows of its table: I do not translate a copy number into "
                        + "those, and I refuse them by name.\n\nFROM A TWO-DIRECTION GRID I DO NOT REMOVE COPIES (linear, circular or curve-driven): "
                        + "SOLIDWORKS numbers them row by row, and a single number does not say "
                        + "which position. I also refuse if after the change the pattern goes "
                        + "into error, is left without faces, or loses faces that are not "
                        + "worth the removed copies — and I put it back as it was. For this reason "
                        + "I also refuse copies FUSED with each other or with the part in "
                        + "a way that changes the number of faces (for example two slots that "
                        + "touch): the face count does not add up and I cannot say what I removed. "
                        + "In the answer «faces_per_copy» says how many faces a copy is "
                        + "worth (null if I could not read it: then this check was not done).",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "the PATTERN to remove copies from (for example «L3»), "
                                    + "not the repeated feature. The identifiers are in the document "
                                    + "state, inside «feature»."),
                            "copies", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "the numbers of the copies to remove, counting from 1 after "
                                    + "the original. For example [2] removes the second copy."),
                            "reactivate", O(
                                "type", "boolean",
                                "description", "true to PUT BACK copies removed earlier, instead of removing "
                                    + "more. Default false.")),
                        "required", A("feature_uid", "copies")),
                },
                new OperationSpec
                {
                    Name = "engrave_text",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Engraves (or embosses) a text on a flat face, and "
                        + "refuses if the text does not fit.",
                    Description =
                        "Writes WORDS into the part: serial number, mounting direction, customer "
                        + "name, «UP». Engraved (default) or embossed."
                        + "\n\nI CHECK THAT IT FITS FIRST, AND IT IS HALF OF THE JOB. The width of a text "
                        + "cannot be calculated from the string: it depends on the font, and "
                        + "«PROVA» 10 mm high in Century Gothic is 47 mm wide. A text that "
                        + "runs off the face is cut away SILENTLY -- the feature comes out green, "
                        + "and in the part there is «PRO». I read where the letters really "
                        + "end and I refuse, telling you by how much it overflowed.\n\nI "
                        + "ANSWER HOW WIDE AND HIGH IT REALLY IS, so the second time you "
                        + "already know which numbers to pass.\n\nThe anchor is the start "
                        + "of the text at the bottom left: from there it goes to the right. "
                        + "The area of the letters that I answer goes with the "
                        + "square of the height -- doubling «height_mm» quadruples the area "
                        + "-- and it is the way to check two markings against each other."
                        + "\n\nNOT ON A CURVED FACE: wrapped text is another feature, "
                        + "which I do not have.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "text", O(
                                "type", "string",
                                "description", "the text, for example «SN-0042» or «UP»."),
                            "face_uid", O(
                                "type", "string",
                                "description", "the flat face to write on (for example «F1»). "
                                    + "The identifiers are in the document state."),
                            "x_mm", O(
                                "type", "number",
                                "description", "where the text starts on the face, in mm, in the "
                                    + "coordinates of the face. Default 0."),
                            "y_mm", O(
                                "type", "number",
                                "description", "the other coordinate of the anchor. Default 0. The anchor "
                                    + "is the BOTTOM LEFT CORNER of the text, not its center."),
                            "height_mm", O(
                                "type", "number",
                                "description", "the height of the letters in mm. Default 5."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "how deep to engrave, or how high to raise if «raised». "
                                    + "Default 0.5 mm, which is a marking you can read. For one you "
                                    + "can feel by touch you need 0.3-0.8 mm."),
                            "raised", O(
                                "type", "boolean",
                                "description", "true for an embossed text instead of an engraved one. Default "
                                    + "false. Embossed prints better in 3D, engraved reads "
                                    + "better on a machined part."),
                            "font", O(
                                "type", "string",
                                "description", "the name of the font, for example «Arial». If you do not "
                                    + "say, I use the document's. A font that is not on this computer "
                                    + "changes the width of the text."),
                            "bold", O(
                                "type", "boolean",
                                "description", "true for bold: thicker letters, which read better "
                                    + "on a 3D print or a casting. Default false.")),
                        "required", A("text", "face_uid")),
                },
                new OperationSpec
                {
                    Name = "set_property",
                    Family = "identity",
                    Example = "{\"name\":\"NumeroDisegno\",\"value_text\":\"DIS-1234\"}",   // from `the_two_lists_do_not_see_each_other`
                    RequestedDocument = "any",
                    Tested = true,
                    Summary =
                        "Writes a custom property -- drawing number, revision, "
                        + "customer -- on the document or on a configuration.",
                    Description =
                        "Writes a custom property: it is what ends up in the title block of "
                        + "the drawing and in the PDM data card. «Numero disegno» (drawing "
                        + "number), «Revision», «Cliente» (customer), «Descrizione» (description)."
                        + "\n\nTHE DOCUMENT AND A CONFIGURATION ARE TWO SEPARATE DRAWERS. A "
                        + "property written on the document is NOT SEEN from a configuration, and "
                        + "the other way round: they are not inherited. If you say «configuration» "
                        + "I write it there and only there; if you do not say it I write it on "
                        + "the document and only on the document. I repeat it to you in the answer "
                        + "every time, because it is the error that gives no error: the "
                        + "drawing number is written on the document, read back from the configuration, it is not there, and nobody says why."
                        + "\n\nIF IT ALREADY "
                        + "EXISTS, I REPLACE IT. I do not let you believe you changed a revision that stayed the one it was."
                        + "\n\nAFTER WRITING I READ BACK. The command "
                        + "answers a code, not the value: if on reading back I do not find what "
                        + "I wrote I fail instead of confirming a data card that is not the one you think."
                        + "\n\nA value written as a formula (for example \"SW-Mass@part.SLDPRT\") is "
                        + "checked as the TEXT written, and resolved_value gives what SOLIDWORKS "
                        + "evaluates it to, when the two differ.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "the name of the property, for example «Numero disegno» or "
                                    + "«Revision». Write it as you want to see it in the title block: the name "
                                    + "is what whoever reads the data card will see."),
                            "value_text", O(
                                "type", "string",
                                "description", "the value to write. Empty string to clear it."),
                            "configuration", O(
                                "type", "string",
                                "description", "the name of the configuration to write it on. If you do not say it "
                                    + "I write it on the DOCUMENT, and that is a different place. The "
                                    + "names are in the document state, inside «configurations».")),
                        "required", A("name", "value_text")),
                },
                new OperationSpec
                {
                    Name = "create_configuration",
                    Family = "identity",
                    Example = "{\"name\":\"Left\",\"details\":\"left bracket\"}",   // from `the_two_lists_do_not_see_each_other`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Makes a variant of the part -- left and right, with and without a hole -- and "
                        + "activates it at once.",
                    Description =
                        "Creates a configuration: the same bracket left and right, with and "
                        + "without a hole, in a single file. It is how a shop that works to "
                        + "drawings asks for a family of parts.\n\nIT CREATES IT AND ACTIVATES "
                        + "IT. SOLIDWORKS moves the document onto the new configuration at once, "
                        + "without being asked: everything you draw afterwards, and everything "
                        + "the state tells you afterwards, is about THAT one. I tell you in "
                        + "the answer. To go back to another one there is «activate_configuration»."
                        + "\n\nTHE NAME I ANSWER WITH IS THE ONE TO USE, and it is not "
                        + "certain to be the one you asked me for: SOLIDWORKS names can have angle "
                        + "brackets inside, and those are part of the name. Report it as it is."
                        + "\n\n"
                        + "I REFUSE IF A CONFIGURATION WITH THAT NAME ALREADY EXISTS. Two configurations with "
                        + "the same name are not a harmless duplicate: the features suppressed "
                        + "in one and active in the other make two different parts with the same "
                        + "name, and the bill of materials counts one.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "the name of the configuration, for example «Left» or "
                                    + "«No hole». If there is already one like that I refuse."),
                            "details", O(
                                "type", "string",
                                "description", "the description that will appear in the list of "
                                    + "configurations."),
                            "comment", O(
                                "type", "string",
                                "description", "a free comment.")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "activate_configuration",
                    Family = "identity",
                    Example = "{\"name\":\"Left\"}",   // from `limit_configuration_that_already_exists`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Switches to a configuration that already exists, and verifies that it "
                        + "really switched.",
                    Description =
                        "Moves the document onto another configuration. From here on what you "
                        + "draw and what the state tells you are about that one."
                        + "\n\nTHE NAME IS NOT WHAT IT SEEMS. The default configuration is often called "
                        + "«Default<As Machined>» and NOT «Default»: what is in angle brackets "
                        + "is part of the name. Asked for «Default», SOLIDWORKS answers "
                        + "no, silently -- and often there is more than one "
                        + "configuration that starts with «Default» (a weldment has the pair «<As "
                        + "Machined>» and «<As Welded>»), so there is not even a right way to "
                        + "guess. I DO NOT GUESS: copy the whole name from «configurations» in "
                        + "the document state. The names come in the language of the "
                        + "installation.\n\nAFTER ASKING I CHECK WHICH "
                        + "ONE IS ACTIVE NOW: it is not enough for me that the command "
                        + "said yes, because working for half an hour on the wrong variant "
                        + "gives no error."
                        + "\n\nI REFUSE IF THE CONFIGURATION DOES NOT EXIST, and I list the "
                        + "ones that do. And the name must be WHOLE: Default is not enough "
                        + "if it is called Default<As Machined>. A partial name I do not guess, "
                        + "because guessing would mean activating the wrong configuration "
                        + "silently.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "the WHOLE name of the configuration to activate, copied from "
                                    + "«configurations» in the document state. Suffix in angle "
                                    + "brackets included, if there is one.")),
                        "required", A("name")),
                },
            };

        /// <summary>
        /// The `anchor` argument (entry E18), the same for the hole and the pocket. It is
        /// a method and not a field: the list above is initialised before the
        /// fields declared after it, and a field would still be null.
        /// </summary>
        private static Node AnchorSchema(string what, bool fromContour = false)
        {
            string point = fromContour
                ? "The point is the CENTER of the contour, and the anchor applies to ONE contour only, " +
                  "round or rectangular and aligned with the axes of the face."
                : "x_mm/y_mm stay required and must name the same point.";
            return O(
                "type", "object",
                "description", "WHAT KEEPS " + what.ToUpperInvariant() + " WHERE IT IS WHEN THE PART CHANGES. Without it, "
                    + "x_mm/y_mm are absolute coordinates: if the plate gets longer, " + what + " stays "
                    + "where it was. For EACH axis, «x» and «y», both required, one of two things: "
                    + "{\"edge_uid\": \"E7\", \"distance_mm\": 20}, the distance from the CENTER to a "
                    + "straight edge of the same face that runs along the other axis; or "
                    + "{\"centered_between\": [\"E3\", \"E5\"]}, halfway between two opposite edges. " + point
                    + "\n\nI REFUSE IF an axis is missing; if x_mm/y_mm and the anchor do not say the same "
                    + "point; if the edge is not straight, is not on the face, does not run along the other "
                    + "axis or lies on the edge of a chamfer or of a fillet (the other face of the edge must be "
                    + "planar and perpendicular, and if I cannot look at it "
                    + "I refuse); if the face you work on is not planar; if the distance is less than "
                    + "half the width (the profile would straddle the edge); "
                    + "if, on a centred axis, the profile is wider than the gap between the two edges; "
                    + "if the sketch "
                    + "does not come out fully defined, or does not hold up under a trial change — and then I remove the "
                    + "cut. I also refuse when I cannot do those checks: relations of the "
                    + "sketch I cannot read, or an anchor dimension I cannot read or move (the test "
                    + "would pass without testing anything).",
                "properties", O(
                    "x", AxisOfAnchor("x"),
                    "y", AxisOfAnchor("y")),
                "required", A("x", "y"));
        }

        private static Node AxisOfAnchor(string axis)
        {
            string other = axis == "x" ? "y" : "x";
            return O(
                "type", "object",
                "description", "the anchor along " + axis + "_mm: edge_uid + distance_mm, or centered_between",
                "properties", O(
                    "edge_uid", O(
                        "type", "string",
                        "description", "a straight edge of the face, from the state, that runs along " + other + "_mm"),
                    "distance_mm", O(
                        "type", "number",
                        "description", "from the CENTER of the profile to the edge, measured along " + axis + "_mm"),
                    "centered_between", O(
                        "type", "array",
                        "items", O("type", "string"),
                        "description", "two opposite edges of the face, from the state, that run along "
                            + other + "_mm: the center stays halfway between them")));
        }
    }
}
