from __future__ import annotations

from dataclasses import dataclass, field
from html import escape as html_escape
from pathlib import Path
from typing import Iterable


OUT_FILE = Path("report_assets/chapter3_sequence_diagrams.drawio")
DRAWIO_VERSION = "30.0.4"


def esc(value: object) -> str:
    text = str(value)
    text = text.replace("\n", "__NL__")
    text = html_escape(text, quote=True)
    return text.replace("__NL__", "&#xa;")


def attrs(**items: object) -> str:
    return " ".join(f'{k}="{esc(v)}"' for k, v in items.items() if v is not None)


def mxgeom(x: int | float, y: int | float, w: int | float, h: int | float, relative: bool = False, points: list[tuple[int | float, int | float]] | None = None) -> str:
    parts = [f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"']
    if relative:
        parts[0] = f'<mxGeometry relative="1" as="geometry"'
    if points:
        point_xml = "".join(f'<mxPoint x="{px}" y="{py}" />' for px, py in points)
        return parts[0] + ">" + f"<Array as=\"points\">{point_xml}</Array></mxGeometry>"
    return parts[0] + " />"


def vertex(
    cell_id: int,
    value: str,
    style: str,
    x: int,
    y: int,
    w: int,
    h: int,
    parent: str = "1",
    extra: str = "",
) -> str:
    return (
        f'<mxCell id="{cell_id}" value="{esc(value)}" style="{style}" vertex="1" parent="{parent}">'
        f"{mxgeom(x, y, w, h)}"
        f"{extra}"
        f"</mxCell>"
    )


def edge(
    cell_id: int,
    value: str,
    style: str,
    source: int,
    target: int,
    points: list[tuple[int | float, int | float]] | None = None,
    parent: str = "1",
) -> str:
    geom = mxgeom(0, 0, 0, 0, relative=True, points=points)
    return (
        f'<mxCell id="{cell_id}" value="{esc(value)}" style="{style}" edge="1" parent="{parent}" source="{source}" target="{target}">'
        f"{geom}"
        f"</mxCell>"
    )


def title_banner(cell_id: int, title: str) -> str:
    return vertex(
        cell_id,
        title,
        "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=15;align=left;spacingLeft=10;spacingRight=10;",
        10,
        10,
        360,
        26,
    )


@dataclass
class Participant:
    label: str
    x: int
    width: int = 180
    kind: str = "box"  # box | actor


@dataclass
class Message:
    source: int
    target: int
    y: int
    label: str
    kind: str = "sync"  # sync | return
    points: list[tuple[int | float, int | float]] = field(default_factory=list)
    activation_source: bool = False
    activation_target: bool = False


@dataclass
class Frame:
    kind: str  # loop | alt
    x: int
    y: int
    w: int
    h: int
    label: str
    split_y: int | None = None
    split_label: str | None = None


@dataclass
class Page:
    title: str
    interaction: str
    participants: list[Participant]
    messages: list[Message]
    frames: list[Frame] = field(default_factory=list)


def participant_cells(start_id: int, participants: list[Participant]) -> tuple[list[str], dict[int, int], int]:
    cells: list[str] = []
    ids: dict[int, int] = {}
    cell_id = start_id

    for idx, participant in enumerate(participants):
        ids[idx] = cell_id
        if participant.kind == "actor":
            cells.append(
                vertex(
                    cell_id,
                    f"Lifeline1: {participant.label}",
                    "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;fontStyle=1;fontSize=15;",
                    participant.x - 20,
                    45,
                    40,
                    70,
                )
            )
        else:
            cells.append(
                vertex(
                    cell_id,
                    participant.label,
                    "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=15;align=center;verticalAlign=middle;",
                    participant.x - participant.width // 2,
                    48,
                    participant.width,
                    36,
                )
            )
        cell_id += 1

        # Lifeline
        cells.append(
            vertex(
                cell_id,
                "",
                "shape=line;html=1;strokeColor=#000000;strokeWidth=2;dashed=1;",
                participant.x,
                84,
                0,
                760,
            )
        )
        ids[(idx, "line")] = cell_id
        cell_id += 1

    return cells, ids, cell_id


def activation(cell_id: int, x: int, y: int, h: int = 54, w: int = 16) -> str:
    return vertex(
        cell_id,
        "",
        "rounded=0;whiteSpace=wrap;html=1;fillColor=#d9d9d9;strokeColor=#666666;opacity=90;",
        x - w // 2,
        y,
        w,
        h,
    )


def frame_cells(cell_id: int, frame: Frame) -> tuple[list[str], int]:
    cells: list[str] = []
    outer = vertex(
        cell_id,
        "",
        "rounded=0;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#000000;strokeWidth=2;",
        frame.x,
        frame.y,
        frame.w,
        frame.h,
    )
    cells.append(outer)
    cell_id += 1

    label_w = min(max(210, len(frame.label) * 7 + 30), frame.w - 40)
    label_box = vertex(
        cell_id,
        frame.label,
        "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=13;align=left;spacingLeft=8;",
        frame.x + 10,
        frame.y - 14,
        label_w,
        28,
    )
    cells.append(label_box)
    cell_id += 1

    if frame.split_y is not None:
        sep = vertex(
            cell_id,
            "",
            "shape=line;html=1;strokeColor=#000000;strokeWidth=1;dashed=1;",
            frame.x,
            frame.split_y,
            frame.w,
            0,
        )
        cells.append(sep)
        cell_id += 1

        if frame.split_label:
            split_label = vertex(
                cell_id,
                frame.split_label,
                "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=none;fontStyle=1;fontSize=12;align=left;",
                frame.x + 12,
                frame.split_y + 6,
                220,
                22,
            )
            cells.append(split_label)
            cell_id += 1

    return cells, cell_id


def message_cells(cell_id: int, participants: list[Participant], msg: Message, id_map: dict[int, int]) -> tuple[list[str], int]:
    cells: list[str] = []
    src = participants[msg.source]
    tgt = participants[msg.target]

    src_anchor = vertex(
        cell_id,
        "",
        "ellipse;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#ffffff;opacity=0;",
        src.x - 1,
        msg.y - 1,
        2,
        2,
    )
    cells.append(src_anchor)
    src_anchor_id = cell_id
    cell_id += 1

    tgt_anchor = vertex(
        cell_id,
        "",
        "ellipse;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#ffffff;opacity=0;",
        tgt.x - 1,
        msg.y - 1,
        2,
        2,
    )
    cells.append(tgt_anchor)
    tgt_anchor_id = cell_id
    cell_id += 1

    style = "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;verticalAlign=bottom;"
    if msg.kind == "return":
        style += "dashed=1;endArrow=open;strokeColor=#666666;"
    else:
        style += "endArrow=block;strokeColor=#000000;"

    if msg.points:
        cells.append(edge(cell_id, msg.label, style, src_anchor_id, tgt_anchor_id, points=msg.points))
    else:
        cells.append(edge(cell_id, msg.label, style, src_anchor_id, tgt_anchor_id))
    cell_id += 1

    if msg.activation_source:
        cells.append(activation(cell_id, src.x, msg.y - 14, 48))
        cell_id += 1
    if msg.activation_target:
        cells.append(activation(cell_id, tgt.x, msg.y - 14, 60))
        cell_id += 1

    return cells, cell_id


def build_page(page: Page, page_index: int) -> str:
    parts: list[str] = []
    parts.append(f'<diagram id="page-{page_index}" name="{esc(page.title)}">')
    parts.append(
        '<mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1600" pageHeight="1000" math="0" shadow="0">'
    )
    parts.append("<root>")
    parts.append('<mxCell id="0" />')
    parts.append('<mxCell id="1" parent="0" />')

    cell_id = 2
    parts.append(title_banner(cell_id, f"interaction {page.interaction}"))
    cell_id += 1

    participant_xml, _, cell_id = participant_cells(cell_id, page.participants)
    parts.extend(participant_xml)

    for frame in page.frames:
        frame_xml, cell_id = frame_cells(cell_id, frame)
        parts.extend(frame_xml)

    for msg in page.messages:
        msg_xml, cell_id = message_cells(cell_id, page.participants, msg, {})
        parts.extend(msg_xml)

    parts.append("</root>")
    parts.append("</mxGraphModel>")
    parts.append("</diagram>")
    return "".join(parts)


def build_file(pages: Iterable[Page]) -> str:
    xml_parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<mxfile host="drawio" version="{DRAWIO_VERSION}">',
    ]
    for idx, page in enumerate(pages, start=1):
        xml_parts.append(build_page(page, idx))
    xml_parts.append("</mxfile>")
    return "".join(xml_parts)


def pages() -> list[Page]:
    return [
        Page(
            title="3.1 Quản lý điểm hội viên",
            interaction="SD_QuanLyDiemHoiVien",
            participants=[
                Participant("Quản trị viên", 90, width=150, kind="actor"),
                Participant("Giao diện quản lý", 360, width=190),
                Participant("Dịch vụ hội viên", 670, width=190),
                Participant("Cơ sở dữ liệu", 980, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 650, 120, "loop Kiểm tra hội viên [mỗi bản ghi]"),
                Frame("alt", 235, 336, 650, 280, "alt Cập nhật điểm hội viên", split_y=492, split_label="[không hợp lệ]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Chọn chức năng quản lý điểm", activation_target=True),
                Message(1, 2, 205, "2: Yêu cầu danh sách hội viên", activation_source=True, activation_target=True),
                Message(2, 1, 250, "3: Trả danh sách hội viên", kind="return"),
                Message(0, 1, 305, "4: Nhập điểm cần cập nhật", activation_target=True),
                Message(1, 2, 350, "5: Gửi yêu cầu cập nhật", activation_source=True, activation_target=True),
                Message(2, 3, 395, "6: Kiểm tra và lưu điểm mới", activation_source=True, activation_target=True),
                Message(3, 2, 440, "7: Xác nhận đã lưu", kind="return"),
                Message(2, 1, 520, "8: Báo kết quả cập nhật", kind="return"),
                Message(1, 0, 565, "9: Thông báo cập nhật thành công", kind="return"),
                Message(2, 1, 605, "10: Báo lỗi nếu dữ liệu sai", kind="return"),
            ],
        ),
        Page(
            title="3.2 Quản lý danh mục món",
            interaction="SD_QuanLyDanhMucMon",
            participants=[
                Participant("Quản trị viên", 90, width=150, kind="actor"),
                Participant("Giao diện quản lý", 360, width=190),
                Participant("Dịch vụ danh mục món", 680, width=210),
                Participant("Cơ sở dữ liệu", 1010, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 670, 110, "loop Duyệt danh mục [theo thao tác]"),
                Frame("alt", 235, 330, 670, 300, "alt Thêm / sửa / xóa danh mục", split_y=495, split_label="[không hợp lệ]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Mở chức năng danh mục món", activation_target=True),
                Message(1, 2, 205, "2: Yêu cầu danh sách danh mục", activation_source=True, activation_target=True),
                Message(2, 3, 250, "3: Truy vấn danh mục", activation_source=True, activation_target=True),
                Message(3, 2, 295, "4: Trả dữ liệu danh mục", kind="return"),
                Message(0, 1, 350, "5: Nhập thông tin danh mục", activation_target=True),
                Message(1, 2, 395, "6: Gửi yêu cầu thêm/sửa/xóa", activation_source=True, activation_target=True),
                Message(2, 3, 440, "7: Lưu thay đổi vào CSDL", activation_source=True, activation_target=True),
                Message(3, 2, 485, "8: Xác nhận lưu thành công", kind="return"),
                Message(2, 1, 555, "9: Thông báo kết quả", kind="return"),
                Message(1, 0, 600, "10: Hiển thị trạng thái cho quản trị viên", kind="return"),
            ],
        ),
        Page(
            title="3.3 Áp mã giảm giá",
            interaction="SD_ApMaGiamGia",
            participants=[
                Participant("Khách hàng", 90, width=140, kind="actor"),
                Participant("Giao diện đặt hàng", 360, width=190),
                Participant("Dịch vụ mã giảm giá", 680, width=190),
                Participant("Dịch vụ đơn hàng", 980, width=180),
                Participant("Cơ sở dữ liệu", 1260, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 980, 120, "loop Kiểm tra mã [mỗi lần nhập]"),
                Frame("alt", 235, 336, 980, 290, "alt Xác thực mã giảm giá", split_y=490, split_label="[mã không hợp lệ]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Nhập mã giảm giá", activation_target=True),
                Message(1, 2, 205, "2: Kiểm tra mã với hệ thống", activation_source=True, activation_target=True),
                Message(2, 4, 250, "3: Tra cứu trạng thái mã", activation_source=True, activation_target=True),
                Message(4, 2, 295, "4: Trả thông tin mã", kind="return"),
                Message(2, 3, 350, "5: Áp dụng mức giảm giá", activation_source=True, activation_target=True),
                Message(3, 1, 395, "6: Trả giá trị đơn hàng mới", kind="return"),
                Message(1, 0, 450, "7: Hiển thị đơn giá đã giảm", kind="return"),
                Message(2, 3, 535, "8: Cập nhật tạm tính vào đơn hàng", activation_source=True, activation_target=True),
                Message(3, 2, 580, "9: Xác nhận cập nhật", kind="return"),
                Message(2, 1, 625, "10: Thông báo áp mã thành công", kind="return"),
            ],
        ),
        Page(
            title="3.4 Tìm kiếm món ăn",
            interaction="SD_TimKiemMonAn",
            participants=[
                Participant("Khách hàng", 90, width=140, kind="actor"),
                Participant("Giao diện tìm kiếm", 360, width=190),
                Participant("Dịch vụ tìm kiếm món", 700, width=200),
                Participant("Cơ sở dữ liệu", 1030, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 690, 110, "loop Xử lý từ khóa [mỗi lần tìm kiếm]"),
                Frame("alt", 235, 330, 690, 290, "alt Kết quả tìm kiếm", split_y=490, split_label="[không tìm thấy]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Nhập từ khóa món ăn", activation_target=True),
                Message(1, 2, 205, "2: Gửi yêu cầu tìm kiếm", activation_source=True, activation_target=True),
                Message(2, 3, 250, "3: Truy vấn món theo từ khóa", activation_source=True, activation_target=True),
                Message(3, 2, 295, "4: Trả danh sách phù hợp", kind="return"),
                Message(2, 1, 350, "5: Trả kết quả tìm kiếm", kind="return"),
                Message(1, 0, 395, "6: Hiển thị danh sách món", kind="return"),
                Message(2, 3, 535, "7: Ghi nhận lịch sử tìm kiếm", activation_source=True, activation_target=True),
                Message(3, 2, 580, "8: Xác nhận lưu lịch sử", kind="return"),
            ],
        ),
        Page(
            title="3.5 Xác nhận đơn hàng",
            interaction="SD_XacNhanDonHang",
            participants=[
                Participant("Khách hàng", 90, width=140, kind="actor"),
                Participant("Giao diện đơn hàng", 360, width=190),
                Participant("Dịch vụ đơn hàng", 690, width=190),
                Participant("Cơ sở dữ liệu", 1010, width=170),
            ],
            frames=[
                Frame("loop", 235, 180, 690, 110, "loop Kiểm tra giỏ hàng [trước khi xác nhận]"),
                Frame("alt", 235, 330, 690, 300, "alt Xác nhận đơn hàng", split_y=495, split_label="[đơn không hợp lệ]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Chọn xác nhận đơn hàng", activation_target=True),
                Message(1, 2, 205, "2: Gửi yêu cầu xác nhận", activation_source=True, activation_target=True),
                Message(2, 3, 250, "3: Kiểm tra dữ liệu đơn", activation_source=True, activation_target=True),
                Message(3, 2, 295, "4: Trả trạng thái đơn", kind="return"),
                Message(2, 3, 350, "5: Lưu đơn hàng mới", activation_source=True, activation_target=True),
                Message(3, 2, 395, "6: Xác nhận lưu thành công", kind="return"),
                Message(2, 1, 450, "7: Trả mã đơn hàng", kind="return"),
                Message(1, 0, 520, "8: Thông báo đơn hàng đã xác nhận", kind="return"),
                Message(2, 1, 565, "9: Báo lỗi nếu giỏ hàng trống", kind="return"),
            ],
        ),
        Page(
            title="3.6 Thanh toán chuyển khoản",
            interaction="SD_ThanhToanChuyenKhoan",
            participants=[
                Participant("Khách hàng", 90, width=140, kind="actor"),
                Participant("Giao diện thanh toán", 360, width=200),
                Participant("Dịch vụ thanh toán", 700, width=190),
                Participant("Cổng ngân hàng", 1020, width=180),
                Participant("Cơ sở dữ liệu", 1300, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 1020, 120, "loop Theo dõi giao dịch [chờ phản hồi ngân hàng]"),
                Frame("alt", 235, 338, 1020, 300, "alt Xử lý chuyển khoản", split_y=500, split_label="[thanh toán thất bại]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Chọn phương thức chuyển khoản", activation_target=True),
                Message(1, 2, 205, "2: Tạo yêu cầu thanh toán", activation_source=True, activation_target=True),
                Message(2, 3, 250, "3: Gửi thông tin giao dịch", activation_source=True, activation_target=True),
                Message(3, 2, 295, "4: Trả trạng thái giao dịch", kind="return"),
                Message(2, 4, 350, "5: Lưu thông tin thanh toán", activation_source=True, activation_target=True),
                Message(4, 2, 395, "6: Xác nhận đã lưu", kind="return"),
                Message(2, 1, 450, "7: Cập nhật trạng thái đơn hàng", kind="return"),
                Message(1, 0, 535, "8: Thông báo thanh toán thành công", kind="return"),
                Message(2, 1, 580, "9: Hiển thị lỗi nếu giao dịch lỗi", kind="return"),
            ],
        ),
        Page(
            title="3.7 Phân công tài xế",
            interaction="SD_PhanCongTaiXe",
            participants=[
                Participant("Điều phối viên", 90, width=145, kind="actor"),
                Participant("Giao diện điều phối", 360, width=195),
                Participant("Dịch vụ phân công", 690, width=190),
                Participant("Tài xế", 1010, width=130),
                Participant("Cơ sở dữ liệu", 1280, width=170),
            ],
            frames=[
                Frame("loop", 235, 182, 990, 110, "loop Tìm tài xế phù hợp [theo từng đơn]"),
                Frame("alt", 235, 330, 990, 310, "alt Phân công tài xế", split_y=500, split_label="[không có tài xế phù hợp]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Yêu cầu phân công đơn giao", activation_target=True),
                Message(1, 2, 205, "2: Chuyển thông tin đơn", activation_source=True, activation_target=True),
                Message(2, 4, 250, "3: Tra cứu tài xế khả dụng", activation_source=True, activation_target=True),
                Message(4, 2, 295, "4: Trả danh sách tài xế", kind="return"),
                Message(2, 3, 350, "5: Gửi yêu cầu nhận đơn", activation_source=True, activation_target=True),
                Message(3, 2, 395, "6: Phản hồi chấp nhận / từ chối", kind="return"),
                Message(2, 4, 440, "7: Cập nhật bản ghi phân công", activation_source=True, activation_target=True),
                Message(4, 2, 485, "8: Xác nhận đã lưu", kind="return"),
                Message(2, 1, 550, "9: Thông báo tài xế được phân công", kind="return"),
                Message(1, 0, 595, "10: Hiển thị kết quả cho điều phối viên", kind="return"),
            ],
        ),
        Page(
            title="3.8 Cộng điểm hội viên",
            interaction="SD_CongDiemHoiVien",
            participants=[
                Participant("Hệ thống", 90, width=130, kind="actor"),
                Participant("Giao diện hội viên", 360, width=190),
                Participant("Dịch vụ điểm", 690, width=170),
                Participant("Cơ sở dữ liệu", 980, width=170),
                Participant("Thông báo", 1250, width=150),
            ],
            frames=[
                Frame("loop", 235, 182, 950, 110, "loop Cộng điểm [sau khi đơn hoàn tất]"),
                Frame("alt", 235, 330, 950, 290, "alt Tính điểm hội viên", split_y=492, split_label="[không đủ điều kiện]"),
            ],
            messages=[
                Message(0, 1, 150, "1: Kích hoạt quy trình cộng điểm", activation_target=True),
                Message(1, 2, 205, "2: Yêu cầu tính điểm", activation_source=True, activation_target=True),
                Message(2, 3, 250, "3: Kiểm tra lịch sử giao dịch", activation_source=True, activation_target=True),
                Message(3, 2, 295, "4: Trả dữ liệu đơn hàng", kind="return"),
                Message(2, 3, 350, "5: Tính số điểm được cộng", activation_source=True, activation_target=True),
                Message(3, 2, 395, "6: Xác nhận số điểm mới", kind="return"),
                Message(2, 1, 450, "7: Cập nhật điểm hội viên", kind="return"),
                Message(1, 4, 535, "8: Gửi thông báo cộng điểm", activation_target=True),
                Message(4, 1, 580, "9: Xác nhận đã gửi", kind="return"),
                Message(2, 1, 625, "10: Báo kết quả hoàn tất", kind="return"),
            ],
        ),
    ]


def main() -> None:
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    xml = build_file(pages())
    OUT_FILE.write_text(xml, encoding="utf-8")
    print(f"Wrote {OUT_FILE}")


if __name__ == "__main__":
    main()
