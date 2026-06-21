param(
    [string]$OutFile = "report_assets/chapter3_sequence_diagrams.drawio"
)

$ErrorActionPreference = "Stop"

function XmlEscape {
    param([string]$Text)
    if ($null -eq $Text) { return "" }
    $text = $Text.Replace("`r`n", "__NL__").Replace("`n", "__NL__")
    $text = [System.Security.SecurityElement]::Escape($text)
    return $text.Replace("__NL__", "&#xa;")
}

function New-MxGeom {
    param(
        [int]$X,
        [int]$Y,
        [int]$W,
        [int]$H,
        [switch]$Relative
    )

    if ($Relative) {
        return '<mxGeometry relative="1" as="geometry" />'
    }
    return "<mxGeometry x=`"$X`" y=`"$Y`" width=`"$W`" height=`"$H`" as=`"geometry`" />"
}

function New-Vertex {
    param(
        [int]$Id,
        [string]$Value,
        [string]$Style,
        [int]$X,
        [int]$Y,
        [int]$W,
        [int]$H,
        [string]$Parent = "1"
    )
    $value = XmlEscape $Value
    return "<mxCell id=`"$Id`" value=`"$value`" style=`"$Style`" vertex=`"1`" parent=`"$Parent`">$(New-MxGeom -X $X -Y $Y -W $W -H $H)</mxCell>"
}

function New-Edge {
    param(
        [int]$Id,
        [string]$Value,
        [string]$Style,
        [int]$Source,
        [int]$Target,
        [string]$Parent = "1"
    )
    $value = XmlEscape $Value
    return "<mxCell id=`"$Id`" value=`"$value`" style=`"$Style`" edge=`"1`" parent=`"$Parent`" source=`"$Source`" target=`"$Target`">$(New-MxGeom -Relative)</mxCell>"
}

function New-TitleBanner {
    param([int]$Id, [string]$Title)
    return New-Vertex -Id $Id -Value $Title -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=15;align=left;spacingLeft=10;spacingRight=10;" -X 10 -Y 10 -W 360 -H 26
}

function New-ParticipantCells {
    param(
        [int]$StartId,
        [array]$Participants
    )

    $cells = New-Object System.Collections.Generic.List[string]
    $nextId = $StartId
    foreach ($p in $Participants) {
        if ($p.kind -eq "actor") {
            $cells.Add((New-Vertex -Id $nextId -Value ("Lifeline1: " + $p.label) -Style "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;fontStyle=1;fontSize=15;" -X ($p.x - 20) -Y 45 -W 40 -H 70))
        }
        else {
            $cells.Add((New-Vertex -Id $nextId -Value $p.label -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=15;align=center;verticalAlign=middle;" -X ($p.x - [int]($p.width / 2)) -Y 48 -W $p.width -H 36))
        }
        $nextId++
        $cells.Add((New-Vertex -Id $nextId -Value "" -Style "shape=line;html=1;strokeColor=#000000;strokeWidth=2;dashed=1;" -X $p.x -Y 84 -W 0 -H 760))
        $nextId++
    }
    return ,@($cells, $nextId)
}

function New-Activation {
    param(
        [int]$Id,
        [int]$X,
        [int]$Y,
        [int]$H = 54,
        [int]$W = 16
    )
    return New-Vertex -Id $Id -Value "" -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=#d9d9d9;strokeColor=#666666;opacity=90;" -X ($X - [int]($W / 2)) -Y $Y -W $W -H $H
}

function New-FrameCells {
    param(
        [int]$StartId,
        $Frame
    )

    $cells = New-Object System.Collections.Generic.List[string]
    $nextId = $StartId

    $cells.Add((New-Vertex -Id $nextId -Value "" -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#000000;strokeWidth=2;" -X $Frame.x -Y $Frame.y -W $Frame.w -H $Frame.h))
    $nextId++

    $labelWidth = [Math]::Min([Math]::Max(210, ($Frame.label.Length * 7) + 30), $Frame.w - 40)
    $cells.Add((New-Vertex -Id $nextId -Value $Frame.label -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;strokeWidth=2;fontStyle=1;fontSize=13;align=left;spacingLeft=8;" -X ($Frame.x + 10) -Y ($Frame.y - 14) -W $labelWidth -H 28))
    $nextId++

    if ($null -ne $Frame.split_y) {
        $cells.Add((New-Vertex -Id $nextId -Value "" -Style "shape=line;html=1;strokeColor=#000000;strokeWidth=1;dashed=1;" -X $Frame.x -Y $Frame.split_y -W $Frame.w -H 0))
        $nextId++

        if ($null -ne $Frame.split_label -and $Frame.split_label -ne "") {
            $cells.Add((New-Vertex -Id $nextId -Value $Frame.split_label -Style "rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=none;fontStyle=1;fontSize=12;align=left;" -X ($Frame.x + 12) -Y ($Frame.split_y + 6) -W 240 -H 22))
            $nextId++
        }
    }

    return ,@($cells, $nextId)
}

function New-MessageCells {
    param(
        [int]$StartId,
        [array]$Participants,
        $Message
    )

    $cells = New-Object System.Collections.Generic.List[string]
    $nextId = $StartId
    $src = $Participants[$Message.source]
    $tgt = $Participants[$Message.target]

    $cells.Add((New-Vertex -Id $nextId -Value "" -Style "ellipse;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#ffffff;opacity=0;" -X ($src.x - 1) -Y ($Message.y - 1) -W 2 -H 2))
    $srcId = $nextId
    $nextId++

    $cells.Add((New-Vertex -Id $nextId -Value "" -Style "ellipse;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#ffffff;opacity=0;" -X ($tgt.x - 1) -Y ($Message.y - 1) -W 2 -H 2))
    $tgtId = $nextId
    $nextId++

    $style = "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;verticalAlign=bottom;"
    if ($Message.kind -eq "return") {
        $style += "dashed=1;endArrow=open;strokeColor=#666666;"
    }
    else {
        $style += "endArrow=block;strokeColor=#000000;"
    }

    $cells.Add((New-Edge -Id $nextId -Value $Message.label -Style $style -Source $srcId -Target $tgtId))
    $nextId++

    if ($Message.activation_source) {
        $cells.Add((New-Activation -Id $nextId -X $src.x -Y ($Message.y - 14) -H 48))
        $nextId++
    }
    if ($Message.activation_target) {
        $cells.Add((New-Activation -Id $nextId -X $tgt.x -Y ($Message.y - 14) -H 60))
        $nextId++
    }

    return ,@($cells, $nextId)
}

function Build-Page {
    param(
        [int]$PageIndex,
        $Page
    )

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add(('<diagram id="page-{0}" name="{1}">' -f $PageIndex, (XmlEscape $Page.title)))
    $parts.Add('<mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1600" pageHeight="1000" math="0" shadow="0">')
    $parts.Add("<root>")
    $parts.Add('<mxCell id="0" />')
    $parts.Add('<mxCell id="1" parent="0" />')

    $cellId = 2
    $parts.Add((New-TitleBanner -Id $cellId -Title ("interaction " + $Page.interaction)))
    $cellId++

    $result = New-ParticipantCells -StartId $cellId -Participants $Page.participants
    foreach ($cell in $result[0]) { $parts.Add($cell) }
    $cellId = $result[1]

    foreach ($frame in $Page.frames) {
        $result = New-FrameCells -StartId $cellId -Frame $frame
        foreach ($cell in $result[0]) { $parts.Add($cell) }
        $cellId = $result[1]
    }

    foreach ($message in $Page.messages) {
        $result = New-MessageCells -StartId $cellId -Participants $Page.participants -Message $message
        foreach ($cell in $result[0]) { $parts.Add($cell) }
        $cellId = $result[1]
    }

    $parts.Add("</root>")
    $parts.Add("</mxGraphModel>")
    $parts.Add("</diagram>")
    return ($parts -join "")
}

function Build-File {
    param([array]$Pages)

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('<?xml version="1.0" encoding="UTF-8"?>')
    $parts.Add('<mxfile host="drawio" version="30.0.4">')
    $i = 1
    foreach ($page in $Pages) {
        $parts.Add((Build-Page -PageIndex $i -Page $page))
        $i++
    }
    $parts.Add("</mxfile>")
    return ($parts -join "")
}

$pages = @(
    [ordered]@{
        title = "3.1 Quản lý điểm hội viên"
        interaction = "SD_QuanLyDiemHoiVien"
        participants = @(
            [ordered]@{ label = "Quản trị viên"; x = 90; width = 150; kind = "actor" },
            [ordered]@{ label = "Giao diện quản lý"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ hội viên"; x = 670; width = 190; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 980; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 650; h = 120; label = "loop Kiểm tra hội viên [mỗi bản ghi]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 336; w = 650; h = 280; label = "alt Cập nhật điểm hội viên"; split_y = 492; split_label = "[không hợp lệ]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Chọn chức năng quản lý điểm"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Yêu cầu danh sách hội viên"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 1; y = 250; label = "3: Trả danh sách hội viên"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 0; target = 1; y = 305; label = "4: Nhập điểm cần cập nhật"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 350; label = "5: Gửi yêu cầu cập nhật"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 395; label = "6: Kiểm tra và lưu điểm mới"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 440; label = "7: Xác nhận đã lưu"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 520; label = "8: Báo kết quả cập nhật"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 565; label = "9: Thông báo cập nhật thành công"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 605; label = "10: Báo lỗi nếu dữ liệu sai"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.2 Quản lý danh mục món"
        interaction = "SD_QuanLyDanhMucMon"
        participants = @(
            [ordered]@{ label = "Quản trị viên"; x = 90; width = 150; kind = "actor" },
            [ordered]@{ label = "Giao diện quản lý"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ danh mục món"; x = 680; width = 210; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1010; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 670; h = 110; label = "loop Duyệt danh mục [theo thao tác]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 330; w = 670; h = 300; label = "alt Thêm / sửa / xóa danh mục"; split_y = 495; split_label = "[không hợp lệ]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Mở chức năng danh mục món"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Yêu cầu danh sách danh mục"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 250; label = "3: Truy vấn danh mục"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 295; label = "4: Trả dữ liệu danh mục"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 0; target = 1; y = 350; label = "5: Nhập thông tin danh mục"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 395; label = "6: Gửi yêu cầu thêm/sửa/xóa"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 440; label = "7: Lưu thay đổi vào CSDL"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 485; label = "8: Xác nhận lưu thành công"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 555; label = "9: Thông báo kết quả"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 600; label = "10: Hiển thị trạng thái cho quản trị viên"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.3 Áp mã giảm giá"
        interaction = "SD_ApMaGiamGia"
        participants = @(
            [ordered]@{ label = "Khách hàng"; x = 90; width = 140; kind = "actor" },
            [ordered]@{ label = "Giao diện đặt hàng"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ mã giảm giá"; x = 680; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ đơn hàng"; x = 980; width = 180; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1260; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 980; h = 120; label = "loop Kiểm tra mã [mỗi lần nhập]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 336; w = 980; h = 290; label = "alt Xác thực mã giảm giá"; split_y = 490; split_label = "[mã không hợp lệ]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Nhập mã giảm giá"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Kiểm tra mã với hệ thống"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 4; y = 250; label = "3: Tra cứu trạng thái mã"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 4; target = 2; y = 295; label = "4: Trả thông tin mã"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 350; label = "5: Áp dụng mức giảm giá"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 1; y = 395; label = "6: Trả giá trị đơn hàng mới"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 450; label = "7: Hiển thị đơn giá đã giảm"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 535; label = "8: Cập nhật tạm tính vào đơn hàng"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 580; label = "9: Xác nhận cập nhật"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 625; label = "10: Thông báo áp mã thành công"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.4 Tìm kiếm món ăn"
        interaction = "SD_TimKiemMonAn"
        participants = @(
            [ordered]@{ label = "Khách hàng"; x = 90; width = 140; kind = "actor" },
            [ordered]@{ label = "Giao diện tìm kiếm"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ tìm kiếm món"; x = 700; width = 200; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1030; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 690; h = 110; label = "loop Xử lý từ khóa [mỗi lần tìm kiếm]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 330; w = 690; h = 290; label = "alt Kết quả tìm kiếm"; split_y = 490; split_label = "[không tìm thấy]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Nhập từ khóa món ăn"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Gửi yêu cầu tìm kiếm"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 250; label = "3: Truy vấn món theo từ khóa"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 295; label = "4: Trả danh sách phù hợp"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 350; label = "5: Trả kết quả tìm kiếm"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 395; label = "6: Hiển thị danh sách món"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 535; label = "7: Ghi nhận lịch sử tìm kiếm"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 580; label = "8: Xác nhận lưu lịch sử"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.5 Xác nhận đơn hàng"
        interaction = "SD_XacNhanDonHang"
        participants = @(
            [ordered]@{ label = "Khách hàng"; x = 90; width = 140; kind = "actor" },
            [ordered]@{ label = "Giao diện đơn hàng"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ đơn hàng"; x = 690; width = 190; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1010; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 180; w = 690; h = 110; label = "loop Kiểm tra giỏ hàng [trước khi xác nhận]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 330; w = 690; h = 300; label = "alt Xác nhận đơn hàng"; split_y = 495; split_label = "[đơn không hợp lệ]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Chọn xác nhận đơn hàng"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Gửi yêu cầu xác nhận"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 250; label = "3: Kiểm tra dữ liệu đơn"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 295; label = "4: Trả trạng thái đơn"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 350; label = "5: Lưu đơn hàng mới"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 395; label = "6: Xác nhận lưu thành công"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 450; label = "7: Trả mã đơn hàng"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 520; label = "8: Thông báo đơn hàng đã xác nhận"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 565; label = "9: Báo lỗi nếu giỏ hàng trống"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.6 Thanh toán chuyển khoản"
        interaction = "SD_ThanhToanChuyenKhoan"
        participants = @(
            [ordered]@{ label = "Khách hàng"; x = 90; width = 140; kind = "actor" },
            [ordered]@{ label = "Giao diện thanh toán"; x = 360; width = 200; kind = "box" },
            [ordered]@{ label = "Dịch vụ thanh toán"; x = 700; width = 190; kind = "box" },
            [ordered]@{ label = "Cổng ngân hàng"; x = 1020; width = 180; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1300; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 1020; h = 120; label = "loop Theo dõi giao dịch [chờ phản hồi ngân hàng]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 338; w = 1020; h = 300; label = "alt Xử lý chuyển khoản"; split_y = 500; split_label = "[thanh toán thất bại]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Chọn phương thức chuyển khoản"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Tạo yêu cầu thanh toán"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 250; label = "3: Gửi thông tin giao dịch"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 295; label = "4: Trả trạng thái giao dịch"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 4; y = 350; label = "5: Lưu thông tin thanh toán"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 4; target = 2; y = 395; label = "6: Xác nhận đã lưu"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 450; label = "7: Cập nhật trạng thái đơn hàng"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 535; label = "8: Thông báo thanh toán thành công"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 580; label = "9: Hiển thị lỗi nếu giao dịch lỗi"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.7 Phân công tài xế"
        interaction = "SD_PhanCongTaiXe"
        participants = @(
            [ordered]@{ label = "Điều phối viên"; x = 90; width = 145; kind = "actor" },
            [ordered]@{ label = "Giao diện điều phối"; x = 360; width = 195; kind = "box" },
            [ordered]@{ label = "Dịch vụ phân công"; x = 690; width = 190; kind = "box" },
            [ordered]@{ label = "Tài xế"; x = 1010; width = 130; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 1280; width = 170; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 990; h = 110; label = "loop Tìm tài xế phù hợp [theo từng đơn]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 330; w = 990; h = 310; label = "alt Phân công tài xế"; split_y = 500; split_label = "[không có tài xế phù hợp]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Yêu cầu phân công đơn giao"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Chuyển thông tin đơn"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 4; y = 250; label = "3: Tra cứu tài xế khả dụng"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 4; target = 2; y = 295; label = "4: Trả danh sách tài xế"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 350; label = "5: Gửi yêu cầu nhận đơn"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 395; label = "6: Phản hồi chấp nhận / từ chối"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 4; y = 440; label = "7: Cập nhật bản ghi phân công"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 4; target = 2; y = 485; label = "8: Xác nhận đã lưu"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 550; label = "9: Thông báo tài xế được phân công"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 0; y = 595; label = "10: Hiển thị kết quả cho điều phối viên"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    },
    [ordered]@{
        title = "3.8 Cộng điểm hội viên"
        interaction = "SD_CongDiemHoiVien"
        participants = @(
            [ordered]@{ label = "Hệ thống"; x = 90; width = 130; kind = "actor" },
            [ordered]@{ label = "Giao diện hội viên"; x = 360; width = 190; kind = "box" },
            [ordered]@{ label = "Dịch vụ điểm"; x = 690; width = 170; kind = "box" },
            [ordered]@{ label = "Cơ sở dữ liệu"; x = 980; width = 170; kind = "box" },
            [ordered]@{ label = "Thông báo"; x = 1250; width = 150; kind = "box" }
        )
        frames = @(
            [ordered]@{ kind = "loop"; x = 235; y = 182; w = 950; h = 110; label = "loop Cộng điểm [sau khi đơn hoàn tất]"; split_y = $null; split_label = $null },
            [ordered]@{ kind = "alt"; x = 235; y = 330; w = 950; h = 290; label = "alt Tính điểm hội viên"; split_y = 492; split_label = "[không đủ điều kiện]" }
        )
        messages = @(
            [ordered]@{ source = 0; target = 1; y = 150; label = "1: Kích hoạt quy trình cộng điểm"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 1; target = 2; y = 205; label = "2: Yêu cầu tính điểm"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 2; target = 3; y = 250; label = "3: Kiểm tra lịch sử giao dịch"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 295; label = "4: Trả dữ liệu đơn hàng"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 3; y = 350; label = "5: Tính số điểm được cộng"; kind = "sync"; activation_source = $true; activation_target = $true },
            [ordered]@{ source = 3; target = 2; y = 395; label = "6: Xác nhận số điểm mới"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 450; label = "7: Cập nhật điểm hội viên"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 1; target = 4; y = 535; label = "8: Gửi thông báo cộng điểm"; kind = "sync"; activation_source = $false; activation_target = $true },
            [ordered]@{ source = 4; target = 1; y = 580; label = "9: Xác nhận đã gửi"; kind = "return"; activation_source = $false; activation_target = $false },
            [ordered]@{ source = 2; target = 1; y = 625; label = "10: Báo kết quả hoàn tất"; kind = "return"; activation_source = $false; activation_target = $false }
        )
    }
)

$xml = Build-File -Pages $pages
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
Set-Content -Path $OutFile -Value $xml -Encoding UTF8
Write-Host "Wrote $OutFile"

