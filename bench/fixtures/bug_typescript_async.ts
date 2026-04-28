/**
 * Buggy module: fetchAllUsers does not await Promise.all properly.
 *
 * The function returns a Promise<User[][]> when callers expect Promise<User[]>.
 * The bug is on the .map(...) line: each callback returns a Promise<User>, and
 * the outer Promise.all wraps them, but the missing flatten or missing await
 * inside the body of fetchUserBatches causes the chained logic downstream to
 * receive a Promise[] instead of resolved values.
 */

interface User {
  id: number;
  name: string;
}

async function fetchUser(id: number): Promise<User> {
  return { id, name: `User${id}` };
}

// BUG: Returns Promise<Promise<User>[]> instead of Promise<User[]>
// because the .map produces Promise<User>[], which is wrapped in a single
// Promise — but the inner Promises are never awaited.
async function fetchAllUsers(ids: number[]): Promise<User[]> {
  const promises = ids.map(id => fetchUser(id));
  // Missing: await Promise.all(promises)
  return promises as unknown as User[];  // BUG: type-coerced to satisfy compiler, runtime is wrong
}

// Caller will receive [Promise{...}, Promise{...}] instead of [{id:1,name:"User1"}, ...]
async function summarize(ids: number[]): Promise<string> {
  const users = await fetchAllUsers(ids);
  return users.map(u => u.name).join(",");
}
