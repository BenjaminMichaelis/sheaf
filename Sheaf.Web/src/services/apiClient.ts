type JsonValue =
  | string
  | number
  | boolean
  | null
  | JsonValue[]
  | { [key: string]: JsonValue }

class ApiClient {
  private baseUrl = __API_BASE_URL__ || ''

  async get<T>(url: string): Promise<T | undefined> {
    const response = await fetch(this.baseUrl + url, {
      credentials: 'include',
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    const text = await response.text()

    return text ? JSON.parse(text) : undefined
  }

  async post<T = void>(url: string, data?: JsonValue): Promise<T | undefined> {
    const response = await fetch(this.baseUrl + url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: data === undefined ? undefined : JSON.stringify(data),
    })
    
    if (!response.ok) {
      const error = await response.text()
      throw new Error(error || `HTTP error! status: ${response.status}`)
    }
    
    if (response.status === 204) {
      return undefined
    }
    
    const text = await response.text()

    return text ? JSON.parse(text) : undefined
  }

  async delete<T = void>(url: string): Promise<T | undefined> {
    const response = await fetch(this.baseUrl + url, {
      method: 'DELETE',
      credentials: 'include',
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    if (response.status === 204) {
      return undefined
    }
    
    return response.json()
  }

  async put<T = void>(url: string, data?: JsonValue): Promise<T | undefined> {
    const response = await fetch(this.baseUrl + url, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: data === undefined ? undefined : JSON.stringify(data),
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    if (response.status === 204) {
      return undefined
    }
    
    return response.json()
  }
}

export const apiClient = new ApiClient()
