--!strict
--!native
--!optimize 2
export type Connection = {
	Connected: boolean,
	Disconnect: (self: Connection) -> (),
}

export type Event<T... = ...any> = {
	Connect: (self: Event<T...>, fn: (T...) -> ()) -> Connection,
	Once: (self: Event<T...>, fn: (T...) -> ()) -> Connection,
	Wait: (self: Event<T...>) -> T...,
}

export type UserEvent<T... = ...any> = Event<T...> & {
	Fire: (self: Event<T...>, T...) -> (),
	DisconnectAll: (self: Event<T...>) -> (),
	Destroy: (self: Event<T...>) -> (),
}

type Node<T...> = {
	_fn: (T...) -> (),
	_next: Node<T...>?,
	_prev: Node<T...>?,
	_signal: EventInternal<T...>,
	_connected: boolean,
	_once: boolean,
}

type EventInternal<T...> = {
	_head: Node<T...>?,
	_threads: { [thread]: boolean }?,
} & Event<T...>

type ConnectionInternal<T...> = {
	_signal: EventInternal<T...>,
	_node: Node<T...>,
} & Connection;

const freeThreads: { thread } = {}
const function runHandlerInFreeThread()
	while true do
		local fn, a, b, c, d, e, f, g = coroutine.yield()
		fn(a, b, c, d, e, f, g)
		freeThreads[#freeThreads + 1] = coroutine.running()
	end
end

const function acquireThread()
	const n = #freeThreads
	if n > 0 then
		const thread = freeThreads[n]
		freeThreads[n] = nil
		return thread
	end
	const thread = coroutine.create(runHandlerInFreeThread)
	coroutine.resume(thread)
	return thread
end

const Connection = {}
Connection.__index = Connection

function Connection.new<T...>(Event: EventInternal<T...>, node: Node<T...>): Connection
	local self = setmetatable({
		Connected = true,
		_signal = Event,
		_node = node,
	}, Connection)
	return (self :: any) :: Connection
end

function Connection.Disconnect<T...>(self: ConnectionInternal<T...>)
	if not self.Connected then
		return
	end
	self.Connected = false

	const node = self._node
	node._connected = false

	const prev = node._prev
	const next_ = node._next
	if prev then
		prev._next = next_
	else
		self._signal._head = next_
	end

	if next_ then
		next_._prev = prev
	end

	node._prev = nil
	node._next = nil
end

const Event = {}
Event.__index = Event

function Event.new<T...>(): Event<T...>
	const self = setmetatable({
		_head = nil,
	}, Event)
	return (self :: any) :: Event<T...>
end

function Event.Connect<T...>(self: EventInternal<T...>, fn: (T...) -> ()): Connection
	const node = {
		_fn = fn,
		_next = self._head,
		_prev = nil,
		_signal = self,
		_connected = true,
		_once = false,
	} :: Node<T...>

	if self._head then
		self._head._prev = node
	end
	self._head = node

	return Connection.new(self, node)
end

function Event.Once<T...>(self: EventInternal<T...>, fn: (T...) -> ()): Connection
	local conn: Connection
	const node: Node<T...> = {
		_fn = (nil :: any),
		_next = self._head,
		_prev = nil,
		_signal = self,
		_connected = true,
		_once = true,
	}

	node._fn = function(...)
		if conn.Connected then
			conn:Disconnect()
		end
		fn(...)
	end

	if self._head then
		self._head._prev = node
	end
	self._head = node

	conn = Connection.new(self, node)
	return conn
end

function Event.Wait<T...>(self: EventInternal<T...>): T...
	const thread = coroutine.running()
	local conn: Connection
	conn = self:Connect(function(...)
		conn:Disconnect()
		task.spawn(thread, ...)
	end)
	return coroutine.yield()
end

function Event.Fire<T...>(self: EventInternal<T...>, ...: T...)
	local node = self._head
	while node do
		const next_ = node._next
		if node._connected then
			const thread = acquireThread()
			task.spawn(thread, node._fn, ...)
		end
		node = next_
	end
end

function Event.DisconnectAll<T...>(self: EventInternal<T...>)
	local node = self._head
	while node do
		const next_ = node._next
		node._connected = false
		node._next = nil
		node._prev = nil
		node = next_
	end
	self._head = nil
end

function Event.Destroy<T...>(self: EventInternal<T...>)
	self:DisconnectAll()
end

return {
	new = Event.new,
}