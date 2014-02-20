class CanvasMarquee
	constructor: (@canvas, @canvasOffset, @rectColour) ->
		@canvasContext = @canvas.getContext("2d")
		@canvas.onmousedown = (e) =>
			@canvasOnMouseDown(e)
		@canvas.onmouseup = (e) =>
			@canvasOnMouseUp(e)
		@canvas.onmousemove = (e) =>
			@canvasOnMouseMove(e)

	canvasOnMouseDown: (mouseEv) ->
		@mouseDown = true
		curpos = @getCursorPosition(mouseEv)
		@initPos = curpos
		@canvasContext.clearRect(0,0,@canvas.width,@canvas.height)
		@canvasContext.fillStyle = @rectColour
		@canvasContext.fillRect(@initPos[0], @initPos[1], 1, 1)

	canvasOnMouseUp: (mouseEv) ->
		@mouseDown = false
		curpos = @getCursorPosition(mouseEv)

	canvasOnMouseMove: (mouseEv) ->
		if not @mouseDown 
			return
		curpos = @getCursorPosition(mouseEv)
		@canvasContext.clearRect(0,0,@canvas.width,@canvas.height)
		@canvasContext.fillStyle = @rectColour
		@canvasContext.fillRect(@initPos[0], @initPos[1], curpos[0]-@initPos[0], curpos[1]-@initPos[1])

	getCursorPosition: (mouseEv) ->
		if mouseEv.pageX != undefined && mouseEv.pageY != undefined
			x = mouseEv.pageX
			y = mouseEv.pageY
		else
			x = mouseEv.clientX + document.body.scrollLeft + document.documentElement.scrollLeft
			y = mouseEv.clientY + document.body.scrollTop + document.documentElement.scrollTop
		x -= @canvasOffset.left
		y -= @canvasOffset.top
		x = x * @canvas.width / @canvas.clientWidth
		y = y * @canvas.height / @canvas.clientHeight
		[Math.floor(x),Math.floor(y)]


