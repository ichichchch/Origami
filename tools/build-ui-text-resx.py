import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate translation key: {key}")
        if not isinstance(value, str):
            raise ValueError(f"Translation must be a string: {key}")
        result[key] = value
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("translations", nargs="+", type=Path)
    args = parser.parse_args()
    resource = Path(__file__).resolve().parents[1] / "src/Origami.Web/Resources/UiText.en.resx"
    ET.register_namespace("xsd", "http://www.w3.org/2001/XMLSchema")
    ET.register_namespace("msdata", "urn:schemas-microsoft-com:xml-msdata")
    tree = ET.parse(resource)
    root = tree.getroot()
    nodes = root.findall("data")
    values = unique_pairs((node.attrib["name"], node.findtext("value", "")) for node in nodes)
    incoming = {}
    for path in args.translations:
        entries = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_pairs)
        for key, value in entries.items():
            if key in incoming and incoming[key] != value:
                raise ValueError(f"Conflicting input translation: {key}")
            if key in values and values[key] != value:
                raise ValueError(f"Refusing to overwrite existing translation: {key}")
            incoming[key] = value
    for key, value in sorted(incoming.items()):
        if key not in values:
            node = ET.SubElement(root, "data", {"name": key, "{http://www.w3.org/XML/1998/namespace}space": "preserve"})
            ET.SubElement(node, "value").text = value
    ET.indent(tree, space="  ")
    tree.write(resource, encoding="utf-8", xml_declaration=True)
    print(f"Merged {len(incoming)} keys; {len(root.findall('data'))} translations total.")


if __name__ == "__main__":
    main()
