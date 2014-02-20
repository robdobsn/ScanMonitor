class DocTypeMgr
    constructor: ->
        $('.mustTable').editableTableWidget()
        $('.mustTable').on 'change', =>
            @tableOnChange()
        @docUniqName = location.search.split('docuniq=')[1]
        @getScanInfo()
        $(window).resize =>
            @resizeImageAndCanvas()
        $(".pageImage").load =>
            @resizeImageAndCanvas()
        @canvasMarquee = new CanvasMarquee($(".pageImageCanvas")[0], $(".pageImageCanvas").offset(), "rgba(50,50,0,.3)")

    getScanInfo: ->
        if typeof(@docUniqName) == "undefined"
            return
        $.ajax 'http://localhost:8080/scandocs/get/' + @docUniqName,
            type: 'GET'
            dataType: 'json'
            error: (jqXHR, textStatus, errorThrown) ->
                $('.statusBox').text "Can't get scandocs: #{textStatus}"
            success: (data, textStatus, jqXHR) =>
                @scanInfo = data
                @showDocInfo()
                @getDocTypeList()
                @getDocTypeInfo(@scanInfo.scanDocInfo.docTypeFiled)

    getDocTypeList: ->
        $.ajax 'http://localhost:8080/doctypes/list',
            type: 'GET'
            dataType: 'json'
            error: (jqXHR, textStatus, errorThrown) ->
                $('.statusBox').text "Can't get doctypes list: #{textStatus}"
            success: (data, textStatus, jqXHR) =>
                @docTypeList = data

    getDocTypeInfo: (docType) ->
        docType = "AA Savings"
        $.ajax 'http://localhost:8080/doctypes/get/' + @convertToSlug(docType),
            type: 'GET'
            dataType: 'json'
            error: (jqXHR, textStatus, errorThrown) ->
                $('.statusBox').text "Can't get doctype: #{docType} #{textStatus}"
            success: (data, textStatus, jqXHR) =>
                @docTypeInfo = data
                @showDocTypeInfo()
                $('.statusBox').text "Ok"

    nextPage: ->
        if @curPageNum < @curDocInfo.PageCount
            @showDocPage @curPageNum + 1

    showDocInfo: ->
        if typeof(@scanInfo) == "undefined"
            return
        @curPageNum = 1
        fileName = "file:" + @scanInfo.scanDocInfo.scanPageImageFileBase + "_" + @curPageNum + ".png"
        $(".pageImage").attr("src", fileName)

    showDocTypeInfo: ->
        if typeof(@docTypeInfo) == "undefined"
            $('#docTypeEdit').attr('value', "")
        else
            $('#docTypeEdit').attr('value', @docTypeInfo.docTypeName)
        @createTable("#mustHaveTable", @docTypeInfo.mustHaveTexts, "Y")
        @showRegionsOnImage()

    createTable: (tableClass, patternTextList, tablePrefix) ->
        $(tableClass).empty()
        tdS = "<td tabindex='1' class='mustTableCell' "
        for txtData, rowIdx in patternTextList
            tblStr = "<tr>" +
                tdS + "id='#{tablePrefix}#{rowIdx}txt'" + ">#{txtData.textToMatch}</td>" +
                tdS + "id='#{tablePrefix}#{rowIdx}tlx'" + ">#{txtData.textBounds.topLeftXPercent}</td>" +
                tdS + "id='#{tablePrefix}#{rowIdx}tly'" + ">#{txtData.textBounds.topLeftYPercent}</td>" +
                tdS + "id='#{tablePrefix}#{rowIdx}wid'" + ">#{txtData.textBounds.widthPercent}</td>" +
                tdS + "id='#{tablePrefix}#{rowIdx}hgt'" + ">#{txtData.textBounds.heightPercent}</td>" +
                tdS + "id='#{tablePrefix}#{rowIdx}rect'" + "><object width=30 height=30 data='img/appbar.edit.svg' type='image/svg+xml'></object></td>" +
                "</tr>"
            $(tableClass).append tblStr

    showRegionsOnImage: ->
        if typeof(@docTypeInfo) == "undefined"
            return
        @showRegions(".pageImage", @docTypeInfo.mustHaveTexts, "Y")

    showRegions: (imageClass, patternTextList, tablePrefix) ->
        canvas = $(".pageImageCanvas")[0]
        context = canvas.getContext("2d")
        context.clearRect(0,0,canvas.width, canvas.height)
        context.fillStyle = "rgba(0,80,0,.2)"
        for txtData, rowIdx in patternTextList
            tlx = parseInt($("##{tablePrefix}#{rowIdx}tlx").text())
            tly = parseInt($("##{tablePrefix}#{rowIdx}tly").text())
            wid = parseInt($("##{tablePrefix}#{rowIdx}wid").text())
            hgt = parseInt($("##{tablePrefix}#{rowIdx}hgt").text())
            context.fillRect(tlx, tly, wid, hgt);

    convertToSlug: (text) ->
        text.replace("/[^\w ]+/g",'').replace("/ +/g",'-')

    resizeImageAndCanvas: ->
        $(".pageImage").width($(".pageDisplay").width())
        $(".pageImageCanvas").width($(".pageDisplay").width())
        $(".pageImageCanvas").height($(".pageImage").height())

    tableOnChange: ->
        @showRegionsOnImage()

$(document).ready ->
    docTypeMgr = new DocTypeMgr()
